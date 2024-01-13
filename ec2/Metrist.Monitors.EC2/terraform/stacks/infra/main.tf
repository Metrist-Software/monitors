variable "orchestrator_region" {
  type = string
}

variable "environment_tag" {
  type = string
}

variable "aws_backend_region" {
  type = string
}

locals {
  name_prefix = "${var.environment_tag}-${var.orchestrator_region}"
}


resource "aws_iam_user" "monuser" {
  name = "${local.name_prefix}-ec2-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

data "aws_ami" "ubuntu" {
  most_recent = true

  filter {
    name   = "name"
    values = ["ubuntu/images/hvm-ssd/ubuntu-focal-20.04-amd64-server-20220118"]
  }

  filter {
    name   = "virtualization-type"
    values = ["hvm"]
  }

  owners = ["099720109477"] # Canonical
}

resource "aws_instance" "instance" {
  ami           = data.aws_ami.ubuntu.id
  instance_type = "t2.nano"

  tags = {
    Name       = "${local.name_prefix}-ec2-monitor-persist"
    persistent = true
  }
}

resource "aws_iam_user_policy" "monuser" {
  name = "${local.name_prefix}-ec2-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "ec2:TerminateInstances",
          "ec2:RunInstances",
          "ec2:CreateTags"
        ],
        Effect   = "Allow",
        Resource = [
          "arn:aws:ec2:*:*:instance/*",
          "arn:aws:ec2:*:*:image/*",
          "arn:aws:ec2:*:*:network-interface/*",
          "arn:aws:ec2:*:*:security-group/*",
          "arn:aws:ec2:*:*:subnet/*",
          "arn:aws:ec2:*:*:volume/*"
        ]
      },
      {
          Effect = "Allow",
          Action = [
            "ec2:DescribeInstances"
          ],
          Resource = "*"
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name     = "/${var.environment_tag}/monitors/ec2/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider  = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    persistent_instance_id = aws_instance.instance.id
    ami_id                 = data.aws_ami.ubuntu.id
    aws_access_key_id      = aws_iam_access_key.monuser.id
    aws_secret_access_key  = aws_iam_access_key.monuser.secret
  })
}
