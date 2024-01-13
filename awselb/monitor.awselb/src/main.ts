import { DescribeTasksCommand, ECSClient, ListTasksCommand } from "@aws-sdk/client-ecs"
import { DeregisterTargetsCommand, DescribeTargetHealthCommand, ElasticLoadBalancingV2Client, RegisterTargetsCommand } from "@aws-sdk/client-elastic-load-balancing-v2"
import { timed, Protocol, StepFunction } from '@metrist/protocol'
import { request } from "undici"
import delay from "delay"

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

const config = {
  port: 3000, // kept here, we can technically receive it from TF
  targetGroupArn: '',
  dnsName: '',
  serviceId: '',
  clusterId: '',
  region: ''
}

const cleanupHandler = async function () {}
const teardownHandler = async function () {}

steps.ChangeTargetGroup = async function() {
  const ecsClient = new ECSClient({region: config.region})
  const listTasksResult = await ecsClient.send(new ListTasksCommand({
    cluster: config.clusterId,
    serviceName: config.serviceId
  }))
  const taskArns = listTasksResult.taskArns
  proto.logInfo('Task arns: ' + JSON.stringify(taskArns))

  const describeTasksResult = await ecsClient.send(new DescribeTasksCommand({
    cluster: config.clusterId,
    tasks: taskArns
  }))
  const ipv4s = describeTasksResult?.tasks?.
    map((task) => task.containers)?.
    map((containers) => containers?.[0]).
    map((container) => container?.networkInterfaces)?.
    map((nifs) => nifs?.[0]).
    map((nif) => nif?.privateIpv4Address ?? '')
  proto.logInfo('ECS IPs: ' + JSON.stringify(ipv4s))
  if (ipv4s?.length != 2) {
    throw new Error("Configuration problem: we don't have 2 IPs")
  }

  const elbClient = new ElasticLoadBalancingV2Client({region: config.region})
  const describeTargetGroupsResult = await elbClient.send(new DescribeTargetHealthCommand({
    TargetGroupArn: config.targetGroupArn
  }))
  proto.logInfo('Describe target health: ' + JSON.stringify(describeTargetGroupsResult))
  let theIp: string = ''
  const healthDescriptions = describeTargetGroupsResult.TargetHealthDescriptions
  switch (healthDescriptions?.length ?? 0) {
    case 0:
      theIp = ipv4s[0]
      break;
    case 1:
      const curIp = healthDescriptions?.[0].Target?.Id
      theIp = curIp == ipv4s[0] ? ipv4s[1] : ipv4s[0]
      await elbClient.send(new DeregisterTargetsCommand({
        TargetGroupArn: config.targetGroupArn,
        Targets: [{Id: curIp, Port: config.port}]
      }))
      break;
    default:
      // This should not happen, exit with an error, maybe next time better.
      // TODO deregister all and wait until a steady state (if this happens regularly in prod)
      throw new Error("Unexpected number of targets in target group, not running check")
  }
  // The ECS instances will return their internal hostname. Luckily, the translation is easy :)
  const theIpString = 'ip-' + theIp.replace(/\./g, '-')
  proto.logInfo('We will wait until the load balancer responds from ' + theIpString)

  // Compare: if 0 IPs, add the first one. If 1 IPs, add the other one.
  // Time until we see the other IP pop up
  proto.sendTime(await timed(async () => {
    // Register the new target
    await elbClient.send(new RegisterTargetsCommand({
      TargetGroupArn: config.targetGroupArn,
      Targets: [{ Id: theIp, Port: config.port }]
    }))

    // We want to see 5 correct responses in short succession. We don't have
    // session affinity defined or something like that so the chance is very low
    // that these five will randomly all be from a still-draining node.
    let waitingFor = 5
    while (true) {
      try {
        const { statusCode, body } = await request(`http://${config.dnsName}`)
        const gotIpString = await body.text()
        if (statusCode == 200 && gotIpString.startsWith(theIpString)) {
          waitingFor--
        }
        else {
          waitingFor = 5
          proto.logDebug(`statusCode: ${statusCode}, gotIp: ${gotIpString}, theIp: ${theIpString}, not done yet`)
        }
      }
      catch (e) {
        proto.logInfo(`Got error, retrying ` + JSON.stringify(e))
        waitingFor = 5
      }

      if (waitingFor == 0) {
        proto.logInfo("Got five hits from the new IP in a row, all done")
        break
      }

      await delay(100)
    }
  }))
}

const configCallback = async function(_config: any) {
  config.port = 3000, // kept here, we can technically receive it from TF
  config.targetGroupArn = proto.getConfigValue('awselb', 'TargetGroupArn') ?? ''
  config.dnsName = proto.getConfigValue('awselb', 'DnsName') ?? ''
  config.serviceId = proto.getConfigValue('awselb', 'ServiceId') ?? ''
  config.clusterId = proto.getConfigValue('awselb', 'ClusterId') ?? ''
  config.region = proto.getConfigValue('awselb', 'AWSRegion') ?? process.env.ORCHESTRATOR_REGION ?? ''

  process.env.AWS_ACCESS_KEY_ID = proto.getConfigValue('awselb', 'AWSAccessKeyID') ?? ''
  process.env.AWS_SECRET_ACCESS_KEY = proto.getConfigValue('awselb', 'AWSSecretAccessKey') ?? ''
}

const main = async function() {
  await proto.handshake(configCallback)
  // Note that key ids aren't technical secrets, so it's fine to log, and it may help debugging access issues.
  // `make user` in the correct env will show the configured key.
  proto.logInfo("Running with AWS_ACCESS_KEY " + process.env.AWS_ACCESS_KEY_ID)

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
