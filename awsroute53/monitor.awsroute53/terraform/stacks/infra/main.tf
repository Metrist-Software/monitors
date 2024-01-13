// One hosted zone per region so that the ChangeResourceRecordSets don't hold each other up
resource "aws_route53_zone" "hosted_zone" {
  name = "metrist-awsroute53monitor-${var.environment_tag}-${var.orchestrator_region}.com"
  comment = "Zone used for Metrist awsroute53 monitor"
}

// Persistent record that the monitors try to read
resource "aws_route53_record" "persistent" {
  zone_id = aws_route53_zone.hosted_zone.zone_id
  name    = "persistent"
  type    = "A"
  ttl     = "300"
  records = ["127.0.0.1"]
}

resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awsroute53-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awsroute53-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "route53:ChangeResourceRecordSets",
          "route53:ListResourceRecordSets"
        ]
        Effect   = "Allow"
        Resource = [
          aws_route53_zone.hosted_zone.arn
        ]
      },
      {
        Action = [
          "route53:GetChange"
        ]
        Effect   = "Allow"
        Resource = [
          "*"
        ]
      },
      // Stuff for the S3 state backend
      {
        Action = [
          "s3:GetObject",
          "s3:PutObject",
          "s3:DeleteObject"
        ]
        Effect   = "Allow"
        Resource = [
          "arn:aws:s3:::cmtf-infra/terraform/awsroute53/infra/${var.environment_tag}/${var.orchestrator_region}.statefile"
        ]
      },
      {
        Action = [
          "s3:ListBucket"
        ]
        Effect = "Allow"
        Resource = [
          "arn:aws:s3:::cmtf-infra"
        ]
      }      
    ]
  })
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  name = "/${var.environment_tag}/monitors/awsroute53/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    aws_access_key_id = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
    persistent_record_name = "${aws_route53_record.persistent.name}.${aws_route53_zone.hosted_zone.name}"
    hosted_zone_id = aws_route53_zone.hosted_zone.zone_id
    hosted_zone_ns = join(",", aws_route53_zone.hosted_zone.name_servers)
    hosted_zone_name = aws_route53_zone.hosted_zone.name
  })
}


