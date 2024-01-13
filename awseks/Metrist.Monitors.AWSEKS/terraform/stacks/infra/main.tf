data "aws_caller_identity" "current" {}
data "aws_availability_zones" "available" {}

locals {
  name_prefix = "${var.environment_tag}-${var.orchestrator_region}-awseks"
  account_id = data.aws_caller_identity.current.account_id
}

resource "aws_iam_user" "monuser" {
  name = "${local.name_prefix}-user"
  path = "/MonitoringUsers/"
}

resource "aws_iam_access_key" "monuser" {
  user = aws_iam_user.monuser.name
}

resource "aws_iam_user_policy" "monuser" {
  name = "${var.environment_tag}-${var.orchestrator_region}-awseks-policy"
  user = aws_iam_user.monuser.name

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = [
          "eks:*"
        ],
        Effect = "Allow",
        Resource = "*"
      }
    ]
  })
}

data "aws_eks_cluster" "cluster" {
  name = module.eks.cluster_id
}

data "aws_eks_cluster_auth" "cluster" {
  name = module.eks.cluster_id
}

provider "kubernetes" {
  host                   = data.aws_eks_cluster.cluster.endpoint
  cluster_ca_certificate = base64decode(data.aws_eks_cluster.cluster.certificate_authority[0].data)
  token                  = data.aws_eks_cluster_auth.cluster.token
}

module "eks" {
  source  = "terraform-aws-modules/eks/aws"
  version = "17.24.0"

  cluster_version                   = "1.21"
  cluster_name                      = "${local.name_prefix}"
  vpc_id                            = module.vpc.vpc_id
  create_fargate_pod_execution_role = false
  cluster_endpoint_public_access    = true
  cluster_endpoint_private_access   = true
  subnets                           = concat(module.vpc.private_subnets, module.vpc.public_subnets)

  node_groups = {
    default = {
      desired_capacity = 1
      max_capacity     = 3
      min_capacity     = 1
    }
  }

  workers_group_defaults = {
    instance_type = "t2.small"
  }

  map_users = [
    {
      userarn  = aws_iam_user.monuser.arn,
      username = aws_iam_user.monuser.name,
      groups   = ["system:masters"]
    }
  ]
}

module "vpc" {
  source  = "terraform-aws-modules/vpc/aws"
  version = "2.77.0"

  name                 = local.name_prefix
  cidr                 = "10.0.0.0/16"
  azs                  = data.aws_availability_zones.available.names
  public_subnets       = ["10.0.0.0/24", "10.0.1.0/24"]

  enable_dns_hostnames = true
  enable_dns_support   = true
}

resource "aws_secretsmanager_secret" "secret" {
  provider = aws.main
  recovery_window_in_days = 0
  name = "/${var.environment_tag}/monitors/awseks/${var.orchestrator_region}/secrets"
}

resource "aws_secretsmanager_secret_version" "current" {
  provider = aws.main
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    aws_access_key_id = aws_iam_access_key.monuser.id
    aws_secret_access_key = aws_iam_access_key.monuser.secret
    aws_eks_cluster_name                     = data.aws_eks_cluster.cluster.name
    aws_eks_cluster_server_address            = data.aws_eks_cluster.cluster.endpoint
    aws_eks_cluster_certificate_authority_data = data.aws_eks_cluster.cluster.certificate_authority[0].data
  })
}
