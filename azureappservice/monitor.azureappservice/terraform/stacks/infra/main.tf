resource "azurerm_resource_group" "main" {
  name     = "monitor-${local.short_env}-appservice"
  location = var.orchestrator_region
}

resource "azurerm_service_plan" "main" {
  name                = "monitor-${local.short_env}-asp"
  resource_group_name = azurerm_resource_group.main.name
  location            = var.orchestrator_region
  os_type             = "Linux"
  sku_name            = "B1"
}

resource "azurerm_linux_web_app" "main" {
  name                = "monitor-${local.short_env}-appsvc"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_service_plan.main.location
  service_plan_id     = azurerm_service_plan.main.id

  site_config {
    application_stack {
      docker_image     = "index.docker.io/appsvcsample/python-helloworld"
      docker_image_tag = "latest"
    }
  }

  app_settings = {
    "WEBSITES_ENABLE_APP_SERVICE_STORAGE" = "false"
    "DOCKER_REGISTRY_SERVER_URL"          = "https://index.docker.io"
  }
}

resource "aws_secretsmanager_secret" "main" {
  name = "/${var.environment_tag}/${var.platform}/${var.orchestrator_region}/azureappservice/credentials"
}

resource "aws_secretsmanager_secret_version" "main" {
  secret_id = aws_secretsmanager_secret.main.name

  secret_string = jsonencode({
    "hostname" = azurerm_linux_web_app.main.default_hostname
  })
}
