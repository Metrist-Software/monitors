import { WebSiteManagementClient } from '@azure/arm-appservice'
import { ResourceManagementClient } from '@azure/arm-resources'
import delay from 'delay'
import http, { IncomingMessage } from 'http'
import { performance } from 'perf_hooks'
import { Protocol, StepFunction } from '@metrist/protocol'
import setupCredentials from '@metrist/azure'

// Here to support running in AWS should we want to in the future. No longer in TF as we pass in azureRegion
const getAzureRegionFromEnv = function(env :string) : any {
  switch(env) {
    case "prod":
      // prod uses westus as CDN isn't supported on westus2 on Azure
      return "westus";
    case "prod2":
      return "useast2";
    case "prod-mon-ca-central-1":
      return "canadacentral"
    case "prod-mon-us-east-1":
      return "eastus"
    case "prod-mon-us-west-1":
      return "westus"
    default:
      return null
  }
}

const env = process.env.ENVIRONMENT_TAG ?? `dev-${process.env.USER}`
const azureRegion = process.env.ORCHESTRATOR_REGION ?? getAzureRegionFromEnv(env) ?? "eastus"
const awsRegion = process.env.AWS_BACKEND_REGION ?? `local`
const randomId = Math.floor(Math.random() * 1000000000).toString(32)
const mkName = (s: string) => `metrist-${env}-${s}-${randomId}`
const resourceGroupName = mkName('resources')
const appName = mkName('app')
const servicePlanName = mkName('service_plan')
const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

const MAX_CLEANUP_DELETIONS = 3;

let hostname = ''

let subscriptionId: string
let appClient: WebSiteManagementClient
let resClient: ResourceManagementClient
async function setupInfra() {
  const credentials = await setupCredentials(env, awsRegion)
  appClient = new WebSiteManagementClient(credentials.credential, credentials.subscriptionId) //
  resClient = new ResourceManagementClient(credentials.credential, credentials.subscriptionId)
  subscriptionId = credentials.subscriptionId
}

steps.CreateService = async function() {
  proto.logInfo(`Create service called, this run's random number: ${randomId}`)
  const startTime = performance.now()

  await resClient.resourceGroups.createOrUpdate(resourceGroupName, {
    location: azureRegion
  })

  await appClient.appServicePlans.beginCreateOrUpdateAndWait(resourceGroupName, servicePlanName, {
    location: azureRegion,
    sku: {
      name: 'S1',
      tier: 'Standard'
    },
    kind: 'Linux',
    reserved: true,
    tags: {
      createdat: Date.now().toString(),
    }
  })

  // :facepalm:
  const serverFarmId = `/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.Web/serverfarms/${servicePlanName}`
  // create service
  const service = await appClient.webApps.beginCreateOrUpdateAndWait(resourceGroupName, appName, {
    location: azureRegion,
    serverFarmId: serverFarmId,
    siteConfig: {
      appCommandLine: "",
      linuxFxVersion: "DOCKER|appsvcsample/python-helloworld:latest",
      appSettings: [
        {
          name: "WEBSITES_ENABLE_APP_SERVICE_STORAGE",
          value: "false"
        },
        {
          name: "DOCKER_REGISTRY_SERVER_URL",
          value: "https://index.docker.io"
        }
      ]
    }
  })
  console.log('Result from service create', service)

  if (service.defaultHostName == null) {
    throw new Error('No hostname received from Azure')
  }
  hostname = service.defaultHostName

  proto.sendTime(performance.now() - startTime)
}

steps.PingService = async function() {
  // Allow this to be run either using the previous CreateService appservice or an existing provided one
  if (!hostname) {
    hostname = ensureNotNullOrUndefined(proto.getConfigValue('azureappservice', 'Hostname'))
  }

  proto.logDebug(`Ping service called! Hostname is ${hostname}`)
  const startTime = performance.now()

  while (true) {
    const result = await new Promise<IncomingMessage>((resolve, _reject) =>
      http.get(`http://${hostname}`, (res) =>
        resolve(res)))
    if (result.statusCode == 200) {
      proto.sendTime(performance.now() - startTime)
      return;
    }

    proto.logInfo('Fetch result is not 200, retrying in a second: ' + result.statusCode)
    await delay(1000)
  }
}

steps.DestroyService = async function() {
  proto.logDebug('Destroy service called!')
  const startTime = performance.now()

  // If you destroy the resource group, you destroy everything inside it as well.
  await resClient.resourceGroups.beginDeleteAndWait(resourceGroupName)

  proto.sendTime(performance.now() - startTime)
}

function ensureNotNullOrUndefined<T>(arg: T | null | undefined): T {
  if (arg == null) {
    throw new Error('Must not be null')
  }

  return arg
}

const configCallback = async function(_config: any) {
  await setupInfra()
}

const teardownHandler = async function() {
  // DestroyService should do everything we could do, but
  // just in case that the step fails on a transient error,
  // we run it again
  proto.logInfo("Running teardown")
  steps.DestroyService()
  proto.logInfo("Teardown done")
}

// We delete any metrist-${env} app service resource group that have no createdat tag (old intances)
// If it does have a createdat tag, then we delete and clean it up only if it is at least 5 minutes old
// Note that the long-living TF app service is prepended with `monitor` and will not be affected by this
const cleanupHandler = async function()
{
  let cleanupDeletionCount = 0;
  proto.logInfo(`Performing cleanup for env ${env}`)

  // This can and likely will throw a warning saying that a resource group cannot be found. The SDK populates the resource group names
  // and doesn't handle its own UnhandledPromiseRejectionWarning if the resource group has already been deleted. Tried manually calling
  // .next() on PagedAsyncIterableIterator with a .catch but the warning/error is nested in the SDK and doesn't bubble up so not much we
  // can do here
  for await (const item of appClient.appServicePlans.list()) {
    if (!item.name?.startsWith(`metrist-${env}`)) {
      continue;
    }

    if (item.tags != null && item.tags.createdat != null && item.resourceGroup != null) {
      const diffMs = Date.now() - parseInt(item.tags.createdat)
      if (diffMs > 5*60*1000) {
        proto.logInfo(`${item.name} app service plan is more than 5 minutes old. Removing associated resource group named ${item.resourceGroup}`)
        cleanupDeletionCount = await deleteResourceGroup(item.resourceGroup, cleanupDeletionCount)
      }
    } else {
      if (item.resourceGroup != null) {
        proto.logInfo(`No tags on ${item.name} app service plan. Removing associated resource group named ${item.resourceGroup}`)
        cleanupDeletionCount = await deleteResourceGroup(item.resourceGroup, cleanupDeletionCount)
      }
    }
  }
}

async function deleteResourceGroup(resourceGroupName : string, cleanupDeletionCount : number) : Promise<number> {
  try {
    if (cleanupDeletionCount < MAX_CLEANUP_DELETIONS) {
      // beginDelete here doesn't work, it never deletes anything. Hopefully the cleanup will not have to run that often as this will hold the
      // monitor until it can delete these groups, we'll cap it at 3 just in case.
      await resClient.resourceGroups.beginDeleteAndWait(resourceGroupName)
      return cleanupDeletionCount++
    } else {
      proto.logWarning(`Not deleting resource group ${resourceGroupName} as we're past our MAX_CLEANUP_DELETIONS count of ${MAX_CLEANUP_DELETIONS}`)
      return cleanupDeletionCount
    }
  } catch (error) {
    proto.logWarning(`Unable to delete resource group ${resourceGroupName}`)
    return cleanupDeletionCount
  }
}

const main = async function() {
  await proto.handshake(configCallback)

  let step: string | null = null
  while ((step = await proto.getStep(cleanupHandler, teardownHandler)) != null) {
    proto.logDebug(`Starting step ${step}`)
    await steps[step]()
      .catch(e => proto.sendError(e))
  }
  proto.logInfo('Orchestrator asked me to exit, all done')
  process.exit(0)
}

main()
