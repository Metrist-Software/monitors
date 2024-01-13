
resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-s3-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-s3-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "s3:List*",
        ]
        Effect   = "Allow"
        Resource = [
          "*",
        ]
      },      
      {
        Action = [
          "s3:Create*",
          "s3:Delete*",
          "s3:Get*",
          "s3:Put*",
        ]
        Effect   = "Allow"
        Resource = [
          "arn:aws:s3:::metrist-mon-*",
        ]
      },
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name = "/${var.environment_tag}/monitors/s3/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    aws_access_key_id = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
  })
}
