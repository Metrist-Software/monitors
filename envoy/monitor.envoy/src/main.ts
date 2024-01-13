import { Protocol, StepFunction, timedWithRetries } from '@metrist/protocol'
import axios from 'axios'

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

const config: any = {
  clientAPIKey: null
}

const configCallback = async (_config: any) => {
  config.clientAPIKey = proto.getConfigValue('envoy', 'clientAPIKey')
}

steps.GetEmployees = async () => {
  const messages = {
    exit: 'Call succeeded'
  }
  const { totalTime } = await timedWithRetries(async () => {
    const response = await axios.get('https://api.envoy.com/v1/employees', {
      headers: {
        'X-Api-Key': config.clientAPIKey
      }
    });
    const resultSet = response.data.data
    if (typeof resultSet === 'undefined') {
      proto.logInfo(`Employee list could not be accessed`)
      throw ("Employee list could not be accessed")
    }
    proto.logInfo(`${response.status}: ${response.statusText}`)
    proto.logInfo(messages.exit)
    proto.logInfo(`List of employees is an object: ${response.data}`)
  },
    (result): any => false,
    0
  )
  proto.sendTime(totalTime)
}

steps.GetReservations = async () => {
  const messages = {
    exit: 'Call succeeded'
  }
  const { totalTime } = await timedWithRetries(async () => {
    const response = await axios.get('https://api.envoy.com/v1/reservations', {
      headers: {
        'X-Api-Key': config.clientAPIKey
      }
    });

    const resultSet = response.data.data
    if (typeof resultSet === 'undefined') {
      proto.logInfo(`Reservation list could not be accessed`)
      throw ("Reservation list could not be accessed")
    }
    proto.logInfo(`${response.status}: ${response.statusText}`)
    proto.logInfo(messages.exit)
    proto.logInfo(`List of reservations is an object: ${response.data}`)
  },
    (result): any => false,
    0
  )
  proto.sendTime(totalTime)
}

async function main() {
  await proto.handshake(configCallback)
  let step: string | null = null
  while ((step = await proto.getStep(
    async () => { },
    async () => { }
  )) != null) {
    proto.logDebug(`Starting step ${step}`)
    await steps[step]()
      .catch(err => proto.sendError(err))
  }
  proto.logDebug('Orchestrator asked me to exit, all done')
  process.exit(0)
}

main()
