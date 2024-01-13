locals {
  bucket       = "awscloudfront-monitor-${var.environment_tag}-${var.orchestrator_region}"
  s3_origin_id = "${local.bucket}-origin"
}

resource "aws_s3_bucket" "bucket" {
  bucket = local.bucket
  acl    = "private"
}

resource "aws_cloudfront_origin_access_identity" "oai" {
}

data "aws_iam_policy_document" "s3_policy" {
  statement {
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.bucket.arn}/*"]

    principals {
      type        = "AWS"
      identifiers = [aws_cloudfront_origin_access_identity.oai.iam_arn]
    }
  }
}

resource "aws_s3_bucket_policy" "bucket_policy" {
  depends_on = [
    aws_s3_bucket.bucket,
    aws_cloudfront_origin_access_identity.oai
  ]

  bucket = aws_s3_bucket.bucket.id
  policy = data.aws_iam_policy_document.s3_policy.json
}

resource "aws_cloudfront_distribution" "distribution" {
  depends_on = [
    aws_s3_bucket.bucket,
    aws_cloudfront_origin_access_identity.oai
  ]

  enabled     = true
  price_class = "PriceClass_100"
  comment     = "Distribution for AWSCloudfront Monitor use only"

  origin {
    domain_name = aws_s3_bucket.bucket.bucket_regional_domain_name
    origin_id   = local.s3_origin_id

    s3_origin_config {
      origin_access_identity = aws_cloudfront_origin_access_identity.oai.cloudfront_access_identity_path
    }
  }

  default_cache_behavior {
    allowed_methods  = ["HEAD", "GET"]
    cached_methods   = ["HEAD", "GET"]
    target_origin_id = local.s3_origin_id

    forwarded_values {
      query_string = false

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }

  restrictions {
    geo_restriction {
      restriction_type = "whitelist"
      locations        = ["US", "CA"]
    }
  }
}


resource "aws_s3_bucket_object" "cached_object" {
  depends_on = [
    aws_s3_bucket.bucket
  ]

  bucket        = local.bucket
  cache_control = "Cache-control: max-age=630720000" # 20 years
  key           = "content/immutable/cached.txt"
  source        = "${path.root}/files/cached.txt"
  content_type  = "text/html"
}

resource "aws_iam_user" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awscloudfront-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awscloudfront-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "s3:PutObject",
          "s3:PutObjectAcl",
          "s3:DeleteObject",
          "s3:DeleteObjectVersion"
        ]
        Effect = "Allow"
        Resource = "arn:aws:s3:::${local.bucket}/*"
      },
      {
        Action = [
          "s3:ListBucket"
        ]
        Effect = "Allow"
        Resource = "arn:aws:s3:::${local.bucket}"
      },
      {
        Action = [
          "cloudfront:CreateInvalidation"
        ]
        Effect = "Allow"
        Resource = aws_cloudfront_distribution.distribution.arn
      }
    ]
  })
}



resource "aws_secretsmanager_secret" "secret_config" {
  provider = aws.main
  name = "/${var.environment_tag}/monitors/awscloudfront/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "secret_config_current" {
  provider = aws.main
  secret_id = aws_secretsmanager_secret.secret_config.id
  secret_string = jsonencode({
    aws_access_key_id        = aws_iam_access_key.monuser.id,
    aws_secret_access_key    = aws_iam_access_key.monuser.secret,
    distribution_id          = aws_cloudfront_distribution.distribution.id,
    distribution_domain_name = aws_cloudfront_distribution.distribution.domain_name,
    bucket                   = aws_s3_bucket.bucket.bucket
  })
}
