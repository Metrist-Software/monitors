# Note: Currently only the function deployed to centralus is managed through terraform.
#       The rest have been manually deployed from before, so will need to be taken down
#       and redeployed to receive updates from this
variable "orchestrator_region" {
  type = string
}

variable "environment_tag" {
  type = string
}

locals {
  orchestrator_region = var.orchestrator_region
}

resource "azurerm_resource_group" "rg" {
  name     = "azurefncs${local.short_env}"
  location = var.orchestrator_region
  tags     = {}
}

resource "azurerm_storage_account" "main" {
  name = "azurefncs${local.short_env}"
  resource_group_name = azurerm_resource_group.rg.name
  location = var.orchestrator_region
  account_tier = "Standard"
  account_replication_type = "LRS"
}

resource "azurerm_storage_container" "main" {
  name                  = "app"
  storage_account_name  = azurerm_storage_account.main.name
  container_access_type = "blob"
}

resource "azurerm_service_plan" "main" {
  name = "azurefncs${local.short_env}"
  resource_group_name = azurerm_resource_group.rg.name
  location = var.orchestrator_region
  os_type = "Linux"
  sku_name = "Y1"
}

data "archive_file" "file_function_app" {
  type = "zip"
  source_dir = "../../../testfunction"
  output_path = "function-app.zip"
}

resource "azurerm_storage_blob" "test_function" {
  name = "${filesha256(data.archive_file.file_function_app.output_path)}.zip"
  storage_account_name = azurerm_storage_account.main.name
  storage_container_name = azurerm_storage_container.main.name
  type = "Block"
  source = data.archive_file.file_function_app.output_path
}

data "azurerm_storage_account_blob_container_sas" "app" {
  connection_string = azurerm_storage_account.main.primary_connection_string
  container_name = azurerm_storage_container.main.name

  start = "2023-01-01T00:00:00Z"
  expiry = "2099-01-01T00:00:00Z"

  permissions {
    read = true
    add    = false
    create = false
    write  = false
    delete = false
    list   = false
  }
}

resource "azurerm_linux_function_app" "main" {
  name = "azurefncs${local.short_env}"
  resource_group_name = azurerm_resource_group.rg.name
  location = var.orchestrator_region

  storage_account_name = azurerm_storage_account.main.name
  storage_account_access_key = azurerm_storage_account.main.primary_access_key

  service_plan_id = azurerm_service_plan.main.id

  app_settings = {
    "WEBSITE_RUN_FROM_PACKAGE" = "https://${azurerm_storage_account.main.name}.blob.core.windows.net/${azurerm_storage_container.main.name}/${azurerm_storage_blob.test_function.name}${data.azurerm_storage_account_blob_container_sas.app.sas}",
    "FUNCTIONS_WORKER_RUNTIME" = "node"
  }

  site_config {
    application_stack {
      node_version = "18"
    }
  }
}

data "azurerm_function_app_host_keys" "app" {
  name = "azurefncs${local.short_env}"
  resource_group_name = azurerm_resource_group.rg.name
}

resource "aws_secretsmanager_secret" "azurefncs" {
  name = "/${var.environment_tag}/az/${var.orchestrator_region}/azurefncs/credentials"
}

resource "aws_secretsmanager_secret_version" "azurefncs" {
  secret_id = aws_secretsmanager_secret.azurefncs.id

  secret_string = jsonencode({
    "TestFunctionUrl" = "https://${azurerm_linux_function_app.main.default_hostname}/api/azuretestfunction/",
    "TestFunctionCode" = data.azurerm_function_app_host_keys.app.default_function_key
  })
}

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
  short_env = "${var.environment_tag}${lookup(var.short_envs, var.orchestrator_region)}"
}
