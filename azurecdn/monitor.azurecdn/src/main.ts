import { run, timedWithRetries, timedWithReturn, Protocol, StepFunction } from '@metrist/protocol'
import { ClientSecretCredential } from '@azure/identity'
import { CdnManagementClient } from '@azure/arm-cdn'
import { BlobServiceClient } from '@azure/storage-blob'
import assert, { AssertionError } from 'assert'
import axios from 'axios'
import { v4 as uuid } from 'uuid'

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

let configured = false
const config = {
  env: 'NO-ENV',
  region: 'NO-REGION',
  clientId: '',
  clientSecret: '',
  tenantId: '',
  subscriptionId: '',
  resourceGroupName: '',
  cdnProfileName: '',
  cdnEndpointName: '',
  blobStorageContainerName: '',
  blobStorageConnectionString: '',
  cacheFileName: '',
  workingFileName: '',
}

const cleanupHandler = async () => {
  configIfNeeded()

  const blobServiceClient = BlobServiceClient.fromConnectionString(config.blobStorageConnectionString)
  const containerClient = blobServiceClient.getContainerClient(config.blobStorageContainerName)

  for await (const blob of containerClient.listBlobsFlat()) {
    const blobClient = containerClient.getBlockBlobClient(blob.name)
    if (blob.name !== config.cacheFileName) {
      proto.logInfo(`Deleting ${blob.name}`)
      await blobClient.delete()
    }
  }
}

steps.GetLongCachedFile = async () => {
  configIfNeeded()
  const { time } = await timedWithReturn(() => axios.get(`https://${config.cdnEndpointName}.azureedge.net/${config.blobStorageContainerName}/${config.cacheFileName}`))

  proto.sendTime(time)
}

steps.GetNewFile = async () => {
  configIfNeeded()

  // Upload a new file to the CDN
  const blobServiceClient = BlobServiceClient.fromConnectionString(config.blobStorageConnectionString)
  const containerClient = blobServiceClient.getContainerClient(config.blobStorageContainerName)
  const blobClient = containerClient.getBlockBlobClient(config.workingFileName)

  const data = `Created at: ${new Date().toString()}`
  await blobClient.upload(data, data.length)

  // Get the file through the CDN until it's cached
  const { totalTime } = await timedWithRetries(
    async () => {
      const response = await axios.get(`https://${config.cdnEndpointName}.azureedge.net/content/${config.workingFileName}`)

      // Throw error to retry
      assert.notStrictEqual(response.headers['x-cache'], 'TCP_MISS', 'Got cache miss, expected cache hit')
      assert.strictEqual(response.data, data, "Received wrong data")
    },
    (ex) => ex instanceof AssertionError
  )

  proto.sendTime(totalTime)
}

steps.PurgeFile = async () => {
  configIfNeeded()

  const {
    time,
  } = await timedWithReturn(() => doPurge(`/content/${config.workingFileName}`))

  proto.sendTime(time)
}

steps.UpdateFile = async () => {
  configIfNeeded()

  // Update blob storage file
  const blobServiceClient = BlobServiceClient.fromConnectionString(config.blobStorageConnectionString)
  const containerClient = blobServiceClient.getContainerClient(config.blobStorageContainerName)
  const blobClient = containerClient.getBlockBlobClient(config.workingFileName)

  const data = `Updated at: ${new Date().toString()}`
  await blobClient.upload(data, data.length)

  // Get the file through the CDN until it's cached
  const { totalTime } = await timedWithRetries(
    async () => {
      proto.logInfo("Waiting for update to propagate")
      const response = await axios.get(`https://${config.cdnEndpointName}.azureedge.net/content/${config.workingFileName}`)

      // Throw error to retry
      assert.notStrictEqual(response.headers['x-cache'], 'TCP_MISS', 'Got cache miss, expected cache hit')
      assert.strictEqual(response.data, data, "Received wrong data")
    },
    (ex) => ex instanceof AssertionError
  )

  proto.sendTime(totalTime)
}

steps.DeleteFile = async () => {
  configIfNeeded()

  const blobServiceClient = BlobServiceClient.fromConnectionString(config.blobStorageConnectionString)
  const containerClient = blobServiceClient.getContainerClient(config.blobStorageContainerName)
  const blobClient = containerClient.getBlockBlobClient(config.workingFileName)

  blobClient.delete()

  proto.logInfo("File deleted. Purging cache")
  await doPurge(`/content/${config.workingFileName}`)
  proto.logInfo("Cache purged")

  const { totalTime } = await timedWithRetries(
    async () => {
      proto.logInfo("Waiting for delete to propagate")
      await axios.get(
        `https://${config.cdnEndpointName}.azureedge.net/content/${config.workingFileName}`,
        { validateStatus: (status) => status === 404 } // Only want a 404 after deleting the file. Throws error to retry
      )
    },
    (ex) => true
  )

  proto.sendTime(totalTime)
}

const configureCdnClient = () => {
  const credentials = new ClientSecretCredential(
    config.tenantId,
    config.clientId,
    config.clientSecret
  )

  return new CdnManagementClient(credentials, config.subscriptionId)
}

const doPurge = (path: string) => {
  const cdnClient = configureCdnClient()

  return cdnClient.endpoints.beginPurgeContentAndWait(
    config.resourceGroupName,
    config.cdnProfileName,
    config.cdnEndpointName,
    { contentPaths: [path] }
  )
}

const configIfNeeded = () => {
  if (configured) return

  config.env    = proto.getConfigValue('azurecdn', 'EnvironmentTag') ?? process.env.ENVIRONMENT_TAG ?? 'local'
  config.region = proto.getConfigValue('azurecdn', 'Region') ?? process.env.AWS_BACKEND_REGION ?? 'local-dev'

  config.clientId       = proto.getConfigValue('azurecdn', 'ClientID') ?? ''
  config.clientSecret   = proto.getConfigValue('azurecdn', 'ClientSecret') ?? ''
  config.subscriptionId = proto.getConfigValue('azurecdn', 'SubscriptionID') ?? ''
  config.tenantId       = proto.getConfigValue('azurecdn', 'TenantID') ?? ''

  config.resourceGroupName = proto.getConfigValue('azurecdn', 'ResourceGroupName') ?? ''
  config.cdnProfileName    = proto.getConfigValue('azurecdn', 'CdnProfileName') ?? ''
  config.cdnEndpointName   = proto.getConfigValue('azurecdn', 'CdnEndpointName') ?? ''
  config.cacheFileName     = proto.getConfigValue('azurecdn', 'CacheFileName') ?? ''
  config.workingFileName   = `${uuid()}.txt`

  config.blobStorageContainerName    = proto.getConfigValue('azurecdn', 'BlobStorageContainerName') ?? ''
  config.blobStorageConnectionString = proto.getConfigValue('azurecdn', 'BlobStorageConnectionString') ?? ''

  proto.logInfo('Config done')
  configured = true
}

const main = async () => {
  await run(proto, steps, cleanupHandler)
}

main()
