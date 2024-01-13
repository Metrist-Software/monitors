variable "orchestrator_region" {
  type = string
}

# Environment tag, e.g. prod, dev1
variable "environment_tag" {
  type = string
}

variable "platform" {
  type = string
}

locals {
  orchestrator_region = var.orchestrator_region
}

resource "azurerm_resource_group" "azurecdn" {
  name     = "${local.short_env}azurecdnrg"
  location = var.orchestrator_region
  tags     = {}
}

resource "azurerm_storage_account" "azurecdn" {
  name                     = "${local.short_env}azurecdnstrg"
  location                 = azurerm_resource_group.azurecdn.location
  account_replication_type = "LRS"
  account_tier             = "Standard"
  resource_group_name      = azurerm_resource_group.azurecdn.name
  tags                     = {}
  allow_blob_public_access = true
}

resource "azurerm_storage_container" "azurecdn" {
  name                  = "content"
  storage_account_name  = azurerm_storage_account.azurecdn.name
  container_access_type = "blob"
}

resource "azurerm_storage_blob" "cachefile" {
  name                   = "cachefile.txt"
  storage_account_name   = azurerm_storage_account.azurecdn.name
  storage_container_name = azurerm_storage_container.azurecdn.name
  type                   = "Block"
  source                 = "${path.module}/storage/cache.txt"
}

resource "azurerm_cdn_profile" "azurecdn" {
  name                = "${local.short_env}azurecdnprfl"
  location            = "global"
  resource_group_name = azurerm_resource_group.azurecdn.name
  sku                 = "Standard_Microsoft"
  tags                = {}
}

resource "azurerm_cdn_endpoint" "azurecdn" {
  name                = "${local.short_env}azurecdnendpt"
  location            = azurerm_resource_group.azurecdn.location
  profile_name        = azurerm_cdn_profile.azurecdn.name
  resource_group_name = azurerm_resource_group.azurecdn.name
  origin_host_header  = azurerm_storage_account.azurecdn.primary_blob_host
  tags                = {}
  global_delivery_rule {
    cache_expiration_action {
      behavior = "Override"
      duration = "365.00:00:00" # 1 year, max allowed expiration
    }
  }
  origin {
    name      = azurerm_storage_account.azurecdn.name
    host_name = azurerm_storage_account.azurecdn.primary_blob_host
  }
}

resource "aws_secretsmanager_secret" "azurecdn" {
  name = "/${var.environment_tag}/${var.platform}/${var.orchestrator_region}/azurecdn/credentials"
}

resource "aws_secretsmanager_secret_version" "azurecdn" {
  secret_id = "/${var.environment_tag}/${var.platform}/${var.orchestrator_region}/azurecdn/credentials"

  secret_string = jsonencode({
    "blob-storage-connection-string" = azurerm_storage_account.azurecdn.primary_connection_string
    "blob-storage-container-name"    = azurerm_storage_container.azurecdn.name
    "resource-group-name"            = azurerm_resource_group.azurecdn.name
    "cdn-profile-name"               = azurerm_cdn_profile.azurecdn.name
    "cdn-endpoint-name"              = azurerm_cdn_endpoint.azurecdn.name
    "cache-file-name"                = azurerm_storage_blob.cachefile.name
  })
}

# For use in Azure-friendly names (24 characters max, letters only)
# With env/platform being 6 characters and resources appending up to an additional
# 12 characters, these can be at most 6 characters long
variable "short_envs" {
  type = map(string)
  default = {
    "eastus"         = "eastu"
    "eastus2"        = "eastu2"
    "centralus"      = "cntru"
    "southcentralus" = "scntru"
    "westus"         = "westu"
    "canadacentral"  = "cacntr"
  }
}

locals {
  short_env = "${var.environment_tag}${var.platform}${lookup(var.short_envs, var.orchestrator_region)}"
}
