data "aws_caller_identity" "current" {}
data "aws_availability_zones" "available" {}

locals {
  name_prefix = "${var.environment_tag}-${var.orchestrator_region}-awsecs"
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

// SG to allow incoming connection to Nginx
resource "aws_security_group" "allow_http" {
  name        = "${local.name_prefix}-awsecs-monitor-sg-allow-http"
  description = "Allow incoming HTTP connections"
  vpc_id      = module.vpc.vpc_id
}

resource "aws_security_group_rule" "http_in" {
  security_group_id = aws_security_group.allow_http.id
  type = "ingress"
  from_port   = 0
  to_port     = 80
  protocol    = "tcp"
  cidr_blocks = ["0.0.0.0/0"]
}

resource "aws_security_group_rule" "out" {
  security_group_id = aws_security_group.allow_http.id
  type = "egress"
  from_port   = 0
  to_port     = 0
  protocol    = "-1"
  cidr_blocks = ["0.0.0.0/0"]
}

// Our ECS cluster
resource "aws_ecs_cluster" "awsecs" {
  name = "${local.name_prefix}-awsecs-monitor"
}

// Nginx task definition
resource "aws_ecs_task_definition" "nginx" {
  family = "service"
  network_mode = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu = 256
  memory = 512
  container_definitions = jsonencode([
    {
      name = "nginx"
      image = "nginx"
      portMappings = [
        {
          containerPort = 80
          hostPort = 80
        }
      ]
    }
  ])
}

// Load balancer to make Nginx accessible from the outside
// There's some nasty dependencies going on that require
// create-before-destroy with target groups and therefore
// random names.
resource "aws_lb" "lb" {
  name = "${local.name_prefix}"
  internal = false
  load_balancer_type = "application"
  security_groups = [aws_security_group.allow_http.id]
  subnets = module.vpc.public_subnets
  enable_deletion_protection = false
}

resource "aws_lb_target_group" "target_group" {
  name = "${substr(format("%s-%s", "awsecs-mon-", replace(uuid(), "-", "")), 0, 32)}"
  port = 80
  protocol = "HTTP"
  target_type = "ip"
  vpc_id = module.vpc.vpc_id
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
      {
        "Effect" = "Allow",
        "Action" = "ecs:ListServices",
        "Resource" = "*"
      },
      {
        "Effect" = "Allow",
        "Action" = "ecs:*",
        "Resource" = "arn:aws:ecs:*:${local.account_id}:service*"
      },
      {
        Action = "iam:CreateServiceLinkedRole",
        Effect = "Allow",
        Resource = "arn:aws:iam::*:role/aws-service-role/ecs.amazonaws.com/AWSServiceRoleForECS*",
        Condition = {
          StringLike = {
            "iam:AWSServiceName" = "ecs.amazonaws.com"
          }
        }
      },
      {
        Action = [
          "iam:AttachRolePolicy",
          "iam:PutRolePolicy"
        ],
        Effect = "Allow",
        Resource = "arn:aws:iam::*:role/aws-service-role/ecs.amazonaws.com/AWSServiceRoleForECS*"
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name = "/${var.environment_tag}/monitors/awsecs/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    aws_access_key_id = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
    vpc_security_group_id = aws_security_group.allow_http.id
    aws_ecs_cluster_id = aws_ecs_cluster.awsecs.id
    aws_ecs_task_definition_arn = aws_ecs_task_definition.nginx.arn
    aws_ecs_vpc_public_subnets = join(",", module.vpc.public_subnets)
    aws_ecs_lb_target_group_arn = aws_lb_target_group.target_group.arn
    aws_ecs_lb_dns_name = aws_lb.lb.dns_name
  })
}
