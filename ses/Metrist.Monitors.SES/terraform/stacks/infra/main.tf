variable "environment_tag" {
  type = string
}

variable "orchestrator_region" {
  type = string
}

variable "aws_backend_region" {
  type = string
}

resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-ses-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-ses-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "ses:SendEmail"
        ],
        Effect   = "Allow"
        Resource = "*"
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name     = "/${var.environment_tag}/monitors/ses/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider  = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    to_email              = "ses-monitor@metrist.io"
    from_email            = "support+monitor@metrist.io"
    aws_access_key_id     = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
  })
}
