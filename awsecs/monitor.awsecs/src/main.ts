// Main loop for monitor.
//
import { Protocol, StepFunction, timeAndSend } from "@metrist/protocol"
import http, { IncomingMessage } from 'http'
import delay from 'delay'
import { ECS, ListServicesCommandInput, ListServicesCommandOutput } from '@aws-sdk/client-ecs'

const config = {
  env: '',
  region: '',
  clusterId: '',
  taskDefinitionArn: '',
  securityGroupId: '',
  targetGroupArn: '',
  vpc: {
    public_subnets: [] as string[]
  },
  lbDnsName: ''
}

const randomId = Math.floor(Math.random() * 1000000000).toString(32)
const serviceName = `nginx-${randomId}`
const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

steps.CreateService = async function () {
  proto.logDebug('Create service called!')
  const ecs = new ECS({ region: config.region })
  await timeAndSend(proto, async () => {
    const result = await ecs.createService({
      serviceName: serviceName,
      cluster: config.clusterId,
      taskDefinition: config.taskDefinitionArn,
      desiredCount: 1,
      launchType: 'FARGATE',
      loadBalancers: [
        {
          targetGroupArn: config.targetGroupArn,
          containerName: 'nginx',
          containerPort: 80
        }
      ],
      networkConfiguration: {
        awsvpcConfiguration: {
          subnets: config.vpc.public_subnets,
          assignPublicIp: "ENABLED",
          securityGroups: [
            config.securityGroupId
          ]
        }
      }
    })
    if (result.$metadata?.httpStatusCode != 200) {
      throw new Error(`Unexpected status code creating instance: ${result.$metadata?.httpStatusCode}`)
    }
    return
  })
}

steps.PingService = async function () {
  proto.logDebug(`Ping service called! IP is ${config.lbDnsName}`)
  await timeAndSend(proto, async () => {
    while (true) {
      try {
        const result = await new Promise<IncomingMessage>((resolve, _reject) =>
          http.get(`http://${config.lbDnsName}`, (res) =>
            resolve(res)))
        if (result.statusCode == 200) {
          return;
        }
        else {
          throw new Error(`Unexpected status code creating instance`)
        }
      }
      catch (e) {
        proto.logInfo('Fetch result is not 200, retrying in a second: ' + e)
        await delay(1000)
      }
    }
  })
}

steps.DestroyService = async function () {
  proto.logDebug('Destroy service called!')
  const ecs = new ECS({ region: config.region })
  await timeAndSend(proto, async () => {
    await ecs.updateService({
      cluster: config.clusterId,
      service: serviceName,
      desiredCount: 0
    })

    const result = await ecs.deleteService({
      cluster: config.clusterId,
      service: serviceName
    })
    if (result.$metadata?.httpStatusCode != 200) {
      throw new Error(`Unexpected status code destroying instance: ${result.$metadata?.httpStatusCode}`)
    }
    // Note: if the httpStatusCode is 200, the metadata status is set to "DRAINING", so technically it  hasn't been stopped yet
    // From AWS docs:
    //When you delete a service, if there are still running tasks that require cleanup, the service status moves from ACTIVE to DRAINING, and the service is no longer visible in the console or in the ListServices API operation. After all tasks have transitioned to either STOPPING or STOPPED status, the service status moves from DRAINING to INACTIVE. Services in the DRAINING or INACTIVE status can still be viewed with the DescribeServices API operation. However, in the future, INACTIVE services may be cleaned up and purged from Amazon ECS record keeping, and DescribeServices calls on those services return a ServiceNotFoundException error.
    return
  })
}

const configCallback = async function (_config: any) {
  config.env = proto.getConfigValue('awsecs', 'EnvironmentTag') ?? ''
  config.region = proto.getConfigValue('awsecs', 'Region') ?? ''
  config.clusterId = proto.getConfigValue('awsecs', 'ClusterId') ?? ''
  config.securityGroupId = proto.getConfigValue('awsecs', 'SecurityGroupId') ?? ''
  config.vpc.public_subnets = proto.getConfigValue('awsecs', 'VpcPublicSubnets')?.split(',') ?? []
  config.taskDefinitionArn = proto.getConfigValue('awsecs', 'AwsTaskDefinitionArn') ?? ''
  config.targetGroupArn = proto.getConfigValue('awsecs', 'AwsLbTargetGroupArn') ?? ''
  config.lbDnsName = proto.getConfigValue('awsecs', 'AwsLbDnsName') ?? ''
  process.env.AWS_ACCESS_KEY_ID = proto.getConfigValue('awsecs', 'AwsAccessKeyId') ?? ''
  process.env.AWS_SECRET_ACCESS_KEY = proto.getConfigValue('awsecs', 'AwsSecretAccessKey') ?? ''
}

const teardownHandler = async function () {
  // DestroyService should do everything we could do, but
  // just in case that the step fails on a transient error,
  // we run it again
  await steps.DestroyService()
    .catch(_e => { })
  proto.logInfo("Teardown done")
}

const cleanupHandler = async function () {
  const ecs = new ECS({ region: config.region })

  const oneHourAgoMs = Date.now() - 60 * 60 * 1000;
  proto.logInfo(`Cleanup: removing instances created before ${oneHourAgoMs}`)

  let nextToken = null
  do {
    // Typescript can be complicated... Anything simpler will confuse the crap out of tsc
    let args: ListServicesCommandInput = nextToken == null ? { cluster: config.clusterId } : { nextToken, cluster: config.clusterId }
    const services = await ecs.listServices(args)

    if (!services?.serviceArns) {
      return
    }

    const serviceNames = services.serviceArns.map(arn => {
      // https://docs.aws.amazon.com/AWSJavaScriptSDK/latest/AWS/ECS.html#listServices-property
      // confirms we'll get things like "arn:aws:ecs:us-east-1:012345678910:service/{cluster}/{serviceName}"
      return arn.split("/").pop() ?? ''
    })
    // describe services only returns up to 10. I believe listServices will be default max 10 returned.
    const serviceDetails = await ecs.describeServices({
      cluster: config.clusterId,
      services: serviceNames
    })

    proto.logInfo(`Cleanup: checking ${serviceDetails.services?.length} services...`)

    if (serviceDetails.services) {
      for (const service of serviceDetails.services) {
        // javascript date/time handling is ... _weird_
        const serviceCreatedAtMs = (service.createdAt ?? new Date()).valueOf()
        proto.logInfo(`Cleanup: checking service ${service.serviceName}, created ${service.createdAt}/${serviceCreatedAtMs}`)
        if (serviceCreatedAtMs < oneHourAgoMs) {
          proto.logInfo(`Cleanup: removing service ${service.serviceName} in ECS cluster ${config.clusterId}`)
          await ecs.updateService({
            cluster: config.clusterId,
            service: service.serviceName,
            desiredCount: 0
          })
          await ecs.deleteService({
            cluster: config.clusterId,
            service: service.serviceName
          })
        }
      }
    }

    nextToken = services.nextToken
  } while (nextToken != null)

  proto.logInfo(`Cleanup done`)
}

const main = async function () {
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
