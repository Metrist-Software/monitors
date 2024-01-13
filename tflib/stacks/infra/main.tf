
variable "env" {
  type = string
}

data "aws_availability_zones" "available" {}

// All monitors share a single VPC; we spread it over multiple AZs so
// we can do things like setup a replicated RDS cluster.
module "vpc" {
  source = "terraform-aws-modules/vpc/aws"
  name                             = "${var.env}-monitoring-vpc"
  cidr                             = "10.0.0.0/16"
  azs                              = data.aws_availability_zones.available.names
  public_subnets                   = ["10.0.101.0/24", "10.0.102.0/24"]
  private_subnets                  = ["10.0.1.0/24", "10.0.2.0/24"]
  enable_nat_gateway               = true
  single_nat_gateway               = false
  enable_dns_support               = true
  enable_dns_hostnames             = true
}

// Note that if you add an output here, you probably also want
// to make it available in ../../shared/infra/main.tf

output "vpc" {
  value = module.vpc
}
