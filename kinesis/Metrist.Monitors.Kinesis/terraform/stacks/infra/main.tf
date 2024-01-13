
resource "aws_kinesis_stream" "stream" {
  name        = "${var.environment_tag}-${var.orchestrator_region}-monitor-stream"
  shard_count = 1
}

resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-kinesis-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-kinesis-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "kinesis:*"
        ],
        Effect = "Allow"
        Resource = [
          aws_kinesis_stream.stream.arn
        ]
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name     = "/${var.environment_tag}/monitors/kinesis/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider  = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    stream_name           = aws_kinesis_stream.stream.name
    aws_access_key_id     = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
  })
}
