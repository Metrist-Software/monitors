resource "aws_iam_role" "iam_for_lambda" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awslambda-testfunction-role"
  assume_role_policy = <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Action": "sts:AssumeRole",
      "Principal": {
        "Service": "lambda.amazonaws.com"
      },
      "Effect": "Allow",
      "Sid": ""
    }
  ]
}
EOF
  inline_policy {
    name = "sqs_read_write_queue"

    policy = jsonencode({
      Version = "2012-10-17"
      Statement = [
        {
          Action   = ["sqs:*"]
          Effect   = "Allow"
          Resource = aws_sqs_queue.monitor_queue.arn
        },
      ]
    })
  }
}

resource "aws_iam_policy" "lambda_logging" {
  name        = "${var.environment_tag}-${var.orchestrator_region}-awslambda-logging"
  path        = "/"
  description = "IAM policy for logging from a lambda"
  policy      = <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Action": [
        "logs:CreateLogGroup",
        "logs:CreateLogStream",
        "logs:PutLogEvents"
      ],
      "Resource": "arn:aws:logs:*:*:*",
      "Effect": "Allow"
    }
  ]
}
EOF
}

resource "aws_iam_role_policy_attachment" "lambda_logs" {
  role       = aws_iam_role.iam_for_lambda.name
  policy_arn = aws_iam_policy.lambda_logging.arn
}

resource "aws_sqs_queue" "monitor_queue" {
  name              = "${var.environment_tag}-${var.orchestrator_region}-awslambda-monitor-queue"
  kms_master_key_id = "alias/aws/sqs"
}

resource "aws_lambda_function" "function" {
  function_name    = "${var.environment_tag}-${var.orchestrator_region}-awslambda-testfunction"
  filename         = data.archive_file.testfunction.output_path
  source_code_hash = filebase64sha256(data.archive_file.testfunction.output_path)
  runtime          = "nodejs18.x"
  memory_size      = 128
  timeout          = 30
  handler          = "index.handler"
  role             = aws_iam_role.iam_for_lambda.arn
  depends_on       = [
    aws_iam_role_policy_attachment.lambda_logs,
    data.archive_file.testfunction
  ]
  environment {
    variables = {
      QUEUE_URL = aws_sqs_queue.monitor_queue.url
    }
  }
}

data "archive_file" "testfunction" {
  type        = "zip"
  output_path = "${path.root}/../../../testfunction.zip"
  source_dir  = "${path.root}/../../../testfunction"
}

resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awslambda-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awslambda-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action   = ["lambda:InvokeFunction"],
        Effect   = "Allow"
        Resource = aws_lambda_function.function.arn
      },
      {
        Action   = ["sqs:*"],
        Effect   = "Allow"
        Resource = aws_sqs_queue.monitor_queue.arn
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name     = "/${var.environment_tag}/monitors/awslambda/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider      = aws.main
  secret_id     = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    test_function_arn = aws_lambda_function.function.arn
    queue_url = aws_sqs_queue.monitor_queue.url
    aws_access_key_id     = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
  })
}
