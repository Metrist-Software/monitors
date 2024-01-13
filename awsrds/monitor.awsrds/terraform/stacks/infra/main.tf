data "aws_caller_identity" "current" {}
data "aws_availability_zones" "available" {}

locals {
  name_prefix = "${var.environment_tag}-${var.orchestrator_region}-awsrds"
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

// SG to allow PostgreSQL connections from anywhere
resource "aws_security_group" "allow_dbin" {
  name        = "${local.name_prefix}-monitor-allow-dbin"
  description = "Allow incoming database socket connections"
  vpc_id      = module.vpc.vpc_id
}

resource "aws_security_group_rule" "pgsql_in" {
  security_group_id = aws_security_group.allow_dbin.id
  type = "ingress"
  from_port   = 0
  to_port     = 5432
  protocol    = "tcp"
  cidr_blocks = ["0.0.0.0/0"]
}
resource "aws_security_group_rule" "mysql_in" {
  security_group_id = aws_security_group.allow_dbin.id
  type = "ingress"
  from_port   = 0
  to_port     = 3301
  protocol    = "tcp"
  cidr_blocks = ["0.0.0.0/0"]
}

resource "aws_security_group_rule" "out" {
  security_group_id = aws_security_group.allow_dbin.id
  type = "egress"
  from_port   = 0
  to_port     = 0
  protocol    = "-1"
  cidr_blocks = ["0.0.0.0/0"]
}

// Subnet group to ensure the database lands in our VPC

resource "aws_db_subnet_group" "subnet_group" {
  name = "monitordb-${local.name_prefix}-subnet-group"
  subnet_ids = module.vpc.public_subnets
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
        Action = "rds:*",
        Effect = "Allow"
        Resource = "arn:aws:rds:*:${local.account_id}:*:monitordb-*"
      },
      {
        Action = "rds:DescribeDBInstances",
        Effect = "Allow"
        Resource = "arn:aws:rds:*:${local.account_id}:*:*"
      },
      {
        Action = "iam:CreateServiceLinkedRole",
        Effect = "Allow",
        Resource = "arn:aws:iam::*:role/aws-service-role/rds.amazonaws.com/AWSServiceRoleForRDS",
        Condition = {
          StringLike = {
            "iam:AWSServiceName" = "rds.amazonaws.com"
          }
        }
      },
      {
        Action = [
          "iam:AttachRolePolicy",
          "iam:PutRolePolicy"
        ],
        Effect = "Allow",
        Resource = "arn:aws:iam::*:role/aws-service-role/rds.amazonaws.com/AWSServiceRoleForRDS"
      }
    ]
  })
}


resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name = "/${var.environment_tag}/monitors/awsrds/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    aws_access_key_id = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
    db_subnet_group_name = aws_db_subnet_group.subnet_group.name
    vpc_security_group_id = aws_security_group.allow_dbin.id
  })
}
