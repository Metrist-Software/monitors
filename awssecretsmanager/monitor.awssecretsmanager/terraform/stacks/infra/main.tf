variable "aws_backend_region" {
  type = string
}

variable "orchestrator_region" {
  type = string
}

variable "environment_tag" {
  type = string
}

resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awssecretsmanager-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awssecretsmanager-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "secretsmanager:GetSecretValue",
          "secretsmanager:CreateSecret",
          "secretsmanager:DeleteSecret"
        ]
        Effect = "Allow"
        Resource = [
          "arn:aws:secretsmanager:${var.orchestrator_region}:*:secret:/monitor.awssecretsmanager/${var.environment_tag}/*"
        ]
      },
      {
        Action = [
          "secretsmanager:ListSecrets"
        ]
        Effect = "Allow"
        Resource = [
          "*"
        ]
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name = "/${var.environment_tag}/monitors/awssecretsmanager/${var.orchestrator_region}/secrets"
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
  })
}
