import { Route53Client, ChangeResourceRecordSetsCommand, ListResourceRecordSetsCommand, waitUntilResourceRecordSetsChanged } from "@aws-sdk/client-route-53"
import { timed, Protocol, StepFunction } from '@metrist/protocol'
import { Resolver } from 'dns'

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

const config = {
  region: '',
  route53Client: new Route53Client({})
}

const configCallback = async function(_config: any) {
  config.region = proto.getConfigValue('awsroute53', 'AWSRegion') ?? process.env.ORCHESTRATOR_REGION ?? `local`
  config.route53Client = new Route53Client({ region: config.region })
}

let runtimeVars :
  {
    persistentRecordName : string,
    zoneId : string,
    monitorHostName: string,
    nsServers: string[],
    hostedZoneName : string
  } = {
    persistentRecordName: '',
    zoneId: '',
    monitorHostName: '',
    nsServers: [],
    hostedZoneName: ''
  }

async function setup() {
  const persistentRecordName = proto.getConfigValue('awsroute53', 'PersistentRecordName') ?? ''
  const zoneId = proto.getConfigValue('awsroute53', 'HostedZoneID') ?? ''
  const nsServers = (proto.getConfigValue('awsroute53', 'HostedZoneNS') ?? '').split(",")
  const hostedZoneName = proto.getConfigValue('awsroute53', 'HostedZoneName') ?? ''
  const runtimeVars = {persistentRecordName: persistentRecordName, zoneId: zoneId, nsServers: nsServers, hostedZoneName: hostedZoneName}
  const monitorHostName = `monitorecord-${Date.now()}.${runtimeVars.hostedZoneName}`
  return {...runtimeVars, monitorHostName: monitorHostName}
}

const cleanupHandler = async function ()
{
  proto.logInfo("Running cleanup")
  const command = new ListResourceRecordSetsCommand({
    HostedZoneId: runtimeVars.zoneId,
  })

  const result = await config.route53Client.send(command)
  proto.logInfo(`Got ${result?.ResourceRecordSets?.length} results to check for cleanup`)
  if (result != null && result.ResourceRecordSets != null && result.ResourceRecordSets.length > 0) {
    for (const resourceRecordSet of result?.ResourceRecordSets) {
      if (resourceRecordSet.Name == null) continue;
      if (!resourceRecordSet.Name.startsWith("monitorecord-")) continue;
      proto.logInfo(`Processing ${resourceRecordSet.Name}`)
      const creationDate = parseInt(resourceRecordSet.Name.split('.')[0].replace('monitorecord-', ''))
      const diffMs = Date.now() - creationDate
      if (diffMs > 10*60*1000) {
        const command = new ChangeResourceRecordSetsCommand({
          HostedZoneId: runtimeVars.zoneId,
          ChangeBatch: {
            Changes: [
              {
                Action: 'DELETE',
                ResourceRecordSet: {
                  Name: resourceRecordSet.Name,
                  Type: resourceRecordSet.Type,
                  TTL: resourceRecordSet.TTL,
                  ResourceRecords: resourceRecordSet.ResourceRecords
                }
              }
            ]
          }
        })

        proto.logInfo(`Cleanup: Deleting stale recordSet ${resourceRecordSet.Name}`)
        await config.route53Client.send(command)
      }
    }
  }
}

const teardownHandler = async function () {}

steps.QueryExistingDNSRecordAPI = async function()
{
  proto.logInfo(`Looking for ${runtimeVars.persistentRecordName} on zone ${runtimeVars.zoneId} via API`)
  const command = new ListResourceRecordSetsCommand({
    HostedZoneId: runtimeVars.zoneId,
    StartRecordName: runtimeVars.persistentRecordName
  })

  proto.sendTime(await timed(async () => {
    const result = await config.route53Client.send(command)
    if ((result.ResourceRecordSets || []).length == 0) {
      throw "Cannot find persistent record"
    }
  }))
}

steps.QueryExistingDNSRecord = async function()
{
  proto.logInfo(`Looking for ${runtimeVars.persistentRecordName} on zone ${runtimeVars.zoneId} via resolver and DNS Servers ${JSON.stringify(runtimeVars.nsServers)}`)
  let nsServerIps : string[] = []
  const resolver = new Resolver();
  for (const server of runtimeVars.nsServers) {
    proto.logInfo(`Looking up ${server}`)
    const ips = await resolveHostname(server, resolver)
    for (const ip of ips) {
      nsServerIps.push(ip)
    }
  }
  proto.logInfo(`Resolved NS Servers are ${JSON.stringify(nsServerIps)}`);
  resolver.setServers(nsServerIps)
  proto.sendTime(await timed(async () => {
    const result = await resolveHostname(runtimeVars.persistentRecordName, resolver)
    if ((result || []).length == 0) {
      throw "Cannot find persistent record"
    }
  }))
}

steps.CreateDNSRecord = async function()
{
  const command = new ChangeResourceRecordSetsCommand({
    HostedZoneId: runtimeVars.zoneId,
    ChangeBatch: {
      Changes: [
        {
          Action: 'CREATE',
          ResourceRecordSet: getResourceRecordSet()
        }
      ]
    }
  })

  proto.sendTime(await timed(async () => {
    const result = await config.route53Client.send(command)
    proto.logInfo(JSON.stringify(result))
    await waitUntilResourceRecordSetsChanged(
      { client: config.route53Client, maxWaitTime: Infinity, minDelay: 5, maxDelay: 5 },
      { Id: result.ChangeInfo?.Id }
    )
  }))
}

steps.RemoveDNSRecord = async function()
{
  const command = new ChangeResourceRecordSetsCommand({
    HostedZoneId: runtimeVars.zoneId,
    ChangeBatch: {
      Changes: [
        {
          Action: 'DELETE',
          ResourceRecordSet: getResourceRecordSet()
        }
      ]
    }
  })

  proto.sendTime(await timed(async () => {
    const result = await config.route53Client.send(command)
    proto.logInfo(JSON.stringify(result))
    await waitUntilResourceRecordSetsChanged(
      { client: config.route53Client, maxWaitTime: Infinity, minDelay: 5, maxDelay: 5 },
      { Id: result.ChangeInfo?.Id }
    )
  }))
}

// await wraper for DNS resolution
function resolveHostname(hostname : string, resolver : Resolver) : Promise<string[]> {
  return new Promise((resolve, reject) => {
    resolver.resolve(hostname, (err, addresses) => {
      if (err) reject(err)
      resolve(addresses)
    })
  });
}

function getResourceRecordSet() {
  return {
    Name: runtimeVars.monitorHostName,
    Type: 'A',
    TTL: 300,
    ResourceRecords: [
      {
        Value: "127.0.0.1"
      }
    ]
  }
}

const main = async function() {
  await proto.handshake(configCallback)
  // Allow proto config or local env vars (private synthetic) but prefer proto config values
  process.env.AWS_ACCESS_KEY_ID = proto.getConfigValue('awsroute53', 'AWSAccessKeyID') ?? process.env.AWS_ACCESS_KEY_ID ?? 'ACCESS-KEY-NOT-FOUND'
  process.env.AWS_SECRET_ACCESS_KEY = proto.getConfigValue('awsroute53', 'AWSSecretAccessKey') ?? process.env.AWS_SECRET_ACCESS_KEY ?? 'SECRET-ACCESS-KEY-NOT-FOUND'
  // Note that key ids aren't technical secrets, so it's fine to log, and it may help debugging access issues.
  proto.logInfo("Running with AWS_ACCESS_KEY " + process.env.AWS_ACCESS_KEY_ID)

  runtimeVars = await setup()

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
