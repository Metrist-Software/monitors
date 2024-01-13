// Infrastructure stack for AWS ELB monitor. This is run under a specific
// user in production, make sure to test with that user and update the
// permissions laid out in stacks/user/main.tf if necessary.

data "aws_caller_identity" "current" {}
data "aws_availability_zones" "available" {}

locals {
  name_prefix = "${var.environment_tag}-${var.orchestrator_region}-awselb"
  account_id = data.aws_caller_identity.current.account_id
}

module "vpc" {
  source  = "terraform-aws-modules/vpc/aws"
  version = "2.77.0"

  name                 = local.name_prefix
  cidr                 = "10.0.0.0/16"
  azs                  = data.aws_availability_zones.available.names
  public_subnets       = ["10.0.0.0/24", "10.0.1.0/24"]
  enable_dns_hostnames = true
  enable_dns_support   = true
}

resource "aws_ecs_cluster" "cluster" {
  name = "${local.name_prefix}-monitor"
}

resource "aws_ecs_task_definition" "monitor" {
  family = "${local.name_prefix}-task"
  requires_compatibilities = ["FARGATE"]
  network_mode = "awsvpc"
  # CPU is in millis of a vCPU, so .25
  cpu = 256
  memory = 512
  container_definitions = jsonencode([
    {
      name = "host_echo"
      image = "canarymonitor/host_echo:1.0.0"
      portMappings = [
        {
          containerPort = 3000
          hostPort = 3000
        }
      ]
    }
  ])
}

resource "aws_security_group" "sg" {
  name        = "${local.name_prefix}-monitor-sg"
  description = "Security group for AWS ELB monitor in ${local.name_prefix}"
  vpc_id      = module.vpc.vpc_id
}

resource "aws_security_group_rule" "http_in" {
  security_group_id = aws_security_group.sg.id
  type = "ingress"
  from_port   = 0
  to_port     = 3000
  protocol    = "tcp"
  cidr_blocks = ["0.0.0.0/0"]
}

resource "aws_security_group_rule" "out" {
  security_group_id = aws_security_group.sg.id
  type = "egress"
  from_port   = 0
  to_port     = 0
  protocol    = "-1"
  cidr_blocks = ["0.0.0.0/0"]
}

resource "aws_ecs_service" "monitor" {
  name = "${local.name_prefix}-monitor"
  cluster = aws_ecs_cluster.cluster.id
  task_definition = aws_ecs_task_definition.monitor.arn
  desired_count = 2
  launch_type = "FARGATE"
  network_configuration {
    subnets = module.vpc.public_subnets
    security_groups = [
      aws_security_group.sg.id
    ]
    assign_public_ip = true
  }
}

resource "aws_lb" "lb" {
  name = "${local.name_prefix}-mon"
  internal = false
  load_balancer_type = "application"
  security_groups = [aws_security_group.sg.id]
  subnets = module.vpc.public_subnets
  enable_deletion_protection = false
}

resource "aws_lb_target_group" "target_group" {
  name = "${substr(format("%s-%s", "awselb-monitor", replace(uuid(), "-", "")), 0, 32)}"
  port = 80
  protocol = "HTTP"
  target_type = "ip"
  vpc_id = module.vpc.vpc_id
  # We skip the "draining" state for simplicity. It should not make a difference w.r.t. the
  # time it take for the LB to take on the new state.
  deregistration_delay = 0
  lifecycle {
    create_before_destroy = true
    ignore_changes = [name]
  }
}

resource "aws_lb_listener" "listener" {
  load_balancer_arn = aws_lb.lb.arn
  port = 80
  protocol = "HTTP"

  default_action {
    type = "forward"
    target_group_arn = aws_lb_target_group.target_group.arn
  }
}

//
// Separate stack to create the user, we cannot let the
// monitor do that.
//
// We give the user rights to create the infra as well so
// we can just deploy things and let it rip.
//

resource "aws_iam_user" "monuser" {
  name = "${local.name_prefix}-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${local.name_prefix}-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      // Stuff for the monitor. Note that this is a pretty restrictive set - for example, most
      // changes are not allowed at the moment (change = delete + create) and need to be added
      // as needed.
      {
        Action = [
          "elasticloadbalancing:Describe*",
          "ec2:Describe*",
          "ecs:Describe*",
          "ecs:ListTasks",
          "ecs:CreateCluster",
          "ecs:RegisterTaskDefinition",
          "elasticloadbalancing:CreateTargetGroup",
          "ec2:CreateSecurityGroup"
        ]
        Effect  = "Allow"
        Resource = [
          "*"
        ]
      },
      {
        Action = [
          "ecs:CreateService"
        ]
        Effect = "Allow"
        Resource = [
          "arn:aws:ecs:*:*:service/${local.name_prefix}*"
        ]
      },
      {
        Action = [
          "elasticloadbalancing:CreateLoadBalancer",
          "elasticloadbalancing:ModifyLoadBalancerAttributes",
          "elasticloadbalancing:DeleteLoadBalancer",
          "elasticloadbalancing:SetSecurityGroups",
          "elasticloadbalancing:CreateListener"
        ]
        Effect = "Allow"
        Resource = [
          "arn:aws:elasticloadbalancing:*:*:loadbalancer/app/${local.name_prefix}-mon*"
        ]
      },
      {
        Action = [
          "elasticloadbalancing:DeleteListener"
        ]
        Effect = "Allow"
        Resource = [
          "arn:aws:elasticloadbalancing:*:*:loadbalancer/app/${local.name_prefix}-mon*"
        ]
      },
      {
        Action = [
          "ec2:RevokeSecurityGroupEgress",
          "ec2:AuthorizeSecurityGroup*"
        ]
        Effect = "Allow"
        Resource = [
          // This is way too wide, but security groups only have the autogenerated ID
          // in their ARNs so we cannot restrict it by ARN+name pattern.
          "arn:aws:ec2:*:*:security-group/*"
        ]
      },
      {
        Action = [
          "elasticloadbalancing:ModifyTargetGroupAttributes",
          "elasticloadbalancing:RegisterTargets",
          "elasticloadbalancing:DeregisterTargets"
        ]
        Effect = "Allow"
        Resource = [
          "arn:aws:elasticloadbalancing:*:${local.account_id}:targetgroup/awselb*"
        ]
      },
      // Stuff for the S3 state backend
      {
        Action = [
          "s3:GetObject",
          "s3:PutObject",
          "s3:DeleteObject"
        ]
        Effect   = "Allow"
        Resource = [
          "arn:aws:s3:::cmtf-infra/terraform/monitoring/awselb/${var.cloud_platform}/${var.environment_tag}/${var.orchestrator_region}/infra.statefile"
        ]
      },
      {
        Action = [
          "s3:ListBucket"
        ]
        Effect = "Allow"
        Resource = [
          "arn:aws:s3:::cmtf-infra"
        ]
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name = "/${var.environment_tag}/monitors/awselb/${var.orchestrator_region}/secrets"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "current" {
  provider = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    aws_access_key_id = aws_iam_access_key.monuser.id
    // TODO - use a PGP key instead? This causes the secret to be written
    // to the statefile in S3, which is secure enough but technically an
    // unnecessary extra copy.
    aws_secret_access_key = aws_iam_access_key.monuser.secret
    aws_elb_target_group_arn = aws_lb_target_group.target_group.arn
    aws_elb_dns_name = aws_lb.lb.dns_name
    aws_ecs_service_id = aws_ecs_service.monitor.id
    aws_ecs_cluster_id = aws_ecs_cluster.cluster.id
  })
}

// This is just for debugging.
output "aws_acces_key_id" {
  value = aws_iam_access_key.monuser.id
}
