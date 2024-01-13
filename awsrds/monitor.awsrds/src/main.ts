import { Protocol, StepFunction, timeAndSend } from '@metrist/protocol'
import mysql = require('mysql2/promise')
import delay from "delay"
import { RDS } from '@aws-sdk/client-rds'

const randString = () => Math.floor(Math.random() * 1000000000).toString(32)
const randomId = randString()
const randomPass = randString() + randString() + randString()

const config = {
  env: '',
  region: '',
  securityGroupId: '',
  subnetGroupName: '',
  instanceId: '',
}

let instanceAddress = ''

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

const teardownHandler = async function() {
  // DestroyInstance should do everything we could do, but
  // just in case that the step fails on a transient error,
  // we run it again
  const rds = new RDS({region: config.region})
  await deleteInstance(rds, config.instanceId).catch(_e => {})
  proto.logInfo("Teardown done")
}

const cleanupHandler = async function ()
{
  const rds = new RDS({region: config.region})

  proto.logInfo(`Performing cleanup for env ${config.env}`)

  try {
    const instances = await rds.describeDBInstances({})

    if (instances != null && instances.DBInstances != null) {
      for await (const item of instances.DBInstances) {
        if (!item.DBInstanceIdentifier?.startsWith(`monitordb-${config.env}`)) {
          continue;
        }

        if (item.TagList != null && item.TagList.find(element => element.Key == "createdat") != null) {
          const createdAt = item.TagList.find(element => element.Key == "createdat")?.Value || "0"

          const diffMs = Date.now() - parseInt(createdAt)
          if (diffMs > 15*60*1000) {
            proto.logInfo(`${item.DBInstanceIdentifier} RDS instance is more than 15 minutes old. Deleting.`)
            await deleteInstance(rds, item.DBInstanceIdentifier)
            .catch(e => proto.logInfo(`Could not delete ${item.DBInstanceIdentifier} reason: ${e} `))
          }
        } else {
          proto.logInfo(`No created at tags on ${item.DBInstanceIdentifier}... Deleting.`)
          await deleteInstance(rds, item.DBInstanceIdentifier)
          .catch(e => proto.logInfo(`Could not delete ${item.DBInstanceIdentifier} reason: ${e} `))
        }
      }
    }
  } catch(e) {
    proto.logWarning(`Error while running cleanup for env ${config.env} in ${config.region}. Error ${e}`)
  }
}

steps.CreateInstance = async function() {
  proto.logDebug('Create instance called!')
  const rds = new RDS({region: config.region})

  await timeAndSend(proto, async () => {
    const result = await rds.createDBInstance({
      DBInstanceIdentifier: config.instanceId,

      Engine: 'mysql',
      EngineVersion: '5.7.37',
      DBInstanceClass: 'db.t2.micro',
      AllocatedStorage: 5,

      DBName: 'db',
      MasterUsername: 'user',
      MasterUserPassword: randomPass,

      Port: 3306,
      DBSubnetGroupName: config.subnetGroupName,
      VpcSecurityGroupIds: [config.securityGroupId],
      PubliclyAccessible: true,
      DeletionProtection: false,
      Tags: [{
        Key: "createdat",
        Value: Date.now().toString()
      }]
    })

    if (result.$metadata?.httpStatusCode != 200) {
      throw new Error(`Unexpected status code creating instance: ${result.$metadata?.httpStatusCode}`)
    }

    // Wait until the instance is available and we see an endpoint.
    while (true) {
      proto.logDebug('Polling for instance data')
      const result = await rds.describeDBInstances({
        DBInstanceIdentifier: config.instanceId
      })

      if (result.$metadata.httpStatusCode == 200
          && result.DBInstances != null
          && result.DBInstances?.length == 1) {

        const instance = result.DBInstances[0]
        proto.logDebug(`Instance state: ${instance.DBInstanceStatus}`)

        // Terraform waits until `available` (or `storage-optimization` but that is
        // unlikely at create). We first get `backing-up` though which is the initial
        // backup and I _think_ that the database is available during that backup so
        // we could try. However, this will make the metrics incompatible with the
        // historical Terraform metrics so probably should be considered (and properly
        // researched) during a separate ticket.
        if (instance.DBInstanceStatus == 'available'
            && instance.Endpoint) {

          instanceAddress = instance.Endpoint?.Address ?? ''
          proto.logInfo(`Instance has IP ${instanceAddress}`)
          return;
        }
      }

      // Sleep for a second. This limits the precision of our measurement, but
      // instance creation is going to be on the order of minutes, so this is
      // short enough.
      await delay(1000)
    }
  })
}

steps.PingInstance = async function() {
  proto.logDebug('Ping instance called!')

  await timeAndSend(proto, async () => {
    while (true) {
      try {
        const conn = await mysql.createConnection({
          host: instanceAddress,
          user: 'user',
          password: randomPass,
          database: 'db'
        })
        await conn.execute('select 1 + 1 as sum')
        await conn.end()
        return
      }
      catch (e) {
        proto.logInfo(`Got exception while trying to connect to ${instanceAddress}, retrying after sleep: ${e}`)
        await delay(1000)
      }
    }
  })
}

steps.DestroyInstance = async function() {
  proto.logDebug('Destroy instance called!')
  const rds = new RDS({region: config.region})

  await timeAndSend(proto, async () => {
    // TODO: Should we wait on the delete like we do the create?
    // Delete reports sub 1s times but the actual deletion takes quite a bit longer than that
    await deleteInstance(rds, config.instanceId)
  })
}

const deleteInstance = async function(rds : RDS, instanceId : string)
{
  await rds.deleteDBInstance({
    DBInstanceIdentifier: instanceId,
    DeleteAutomatedBackups: true,
    SkipFinalSnapshot: true
  })
}

const configCallback = async function(_config: any) {
  config.env = proto.getConfigValue('awsrds', 'EnvironmentTag') ?? ''
  config.region = proto.getConfigValue('awsrds', 'Region') ?? ''
  config.securityGroupId = proto.getConfigValue('awsrds', 'SecurityGroupId') ?? ''
  config.subnetGroupName = proto.getConfigValue('awsrds', 'SubnetGroupName') ?? ''
  config.instanceId = `monitordb-${_config.EnvironmentTag}-${randomId}`

  process.env.AWS_ACCESS_KEY_ID = proto.getConfigValue('awsrds', 'AwsAccessKeyId') ?? ''
  process.env.AWS_SECRET_ACCESS_KEY = proto.getConfigValue('awsrds', 'AwsSecretAccessKey') ?? ''
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
