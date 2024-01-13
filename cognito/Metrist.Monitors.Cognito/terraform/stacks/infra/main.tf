resource "aws_cognito_user_pool" "pool" {
  name = "CognitoMonitorPool-${var.environment_tag}-${var.orchestrator_region}"
  password_policy {
    minimum_length = 8
    require_lowercase = false
    require_uppercase = false
    require_numbers = false
    require_symbols = false
  }
}

resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-cognito-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-cognito-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "cognito-idp:*"
        ],
        Effect = "Allow"
        Resource = [
          aws_cognito_user_pool.pool.arn
        ]
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name = "/${var.environment_tag}/${var.orchestrator_region}/monitors/cognito/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    aws_access_key_id = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
    user_pool = aws_cognito_user_pool.pool.id
  })
}
