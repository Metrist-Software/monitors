resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awsiam-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awsiam-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "iam:CreateUser",
          "iam:DeleteUser",
          "iam:AttachUserPolicy",
          "iam:DetachUserPolicy",
          "iam:ListUsers",
          "iam:ListGroupsForUser"
        ]
        Effect   = "Allow"
        Resource = [
          "arn:aws:iam::*:user/AwsIamMonitorTestUsers/*"
        ]
      },
      {
        Action = [
          "iam:CreateGroup",
          "iam:DeleteGroup",
          "iam:AddUserToGroup",
          "iam:RemoveUserFromGroup",
          "iam:ListGroups"
        ]
        Effect   = "Allow"
        Resource = [
          "arn:aws:iam::*:group/AwsIamMonitorTestGroups/*"
        ]
      }
    ]
  })
}

resource "aws_iam_policy" "testpolicy" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awsiam-testpolicy"
  path = "/AwsIamMonitorTestPolicies/"
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          // Something that should be quite innocent in case someone bad gets hold of it.
          "aws-marketplace:DescribeAgreement"
        ]
        Effect   = "Allow"
        Resource = "*"
      }
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name = "/${var.environment_tag}/monitors/awsiam/${var.orchestrator_region}/secrets"
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
