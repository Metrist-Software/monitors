
resource "aws_sqs_queue" "queue" {
  name = "${var.environment_tag}-${var.orchestrator_region}-monitor-queue"
}

resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-sqs-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-sqs-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "sqs:*"
        ],
        Effect = "Allow"
        Resource = [
          aws_sqs_queue.queue.arn
        ]
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name = "/${var.environment_tag}/monitors/sqs/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    queue_url = aws_sqs_queue.queue.url
    aws_access_key_id = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
  })
}
