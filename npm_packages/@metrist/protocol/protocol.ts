//
// Metrist orchestrator<->monitor protocol implementation for NodeJS
//
// Currently largely copied from the C# implementation, might not be the
// most idiomatic code therefore.
//

const major = 1
const minor = 1

const write = function(msg: string) {
  const size = Buffer.byteLength(msg).toString().padStart(5, '0');
  process.stdout.write(`${size} ${msg}`)
}


const read = async function() {
  const _read = async function(nbytes: number) {
    let v = null
    while  (v == null) {
      if (!process.stdin.readable) {
        throw 'stdin not readable, giving up'
      }
      await new Promise(r => setTimeout(r, 100))
      v = process.stdin.read(nbytes)
    }
    return '' + v
  }
  const length = Number(await _read(6))
  let msg = await _read(length)
  return msg
}
const expect = async function(re: RegExp) {
  const msg = await read()
  const result = re.exec(msg)
  if (result == null) {
    throw `Unexpected message; wanted="${re}", received="${msg}"`
  }
  return result
}
const snakeAndUpcase = function(name: string) {
  return name.replace(/[A-Z]/g, '_$&').toUpperCase()
}

export type StepFunction = () => Promise<void>
export type CleanupHandler = () => Promise<void>
export type TeardownHandler = () => Promise<void>

export class Protocol {
  config: Record<string, string> = {}
  major: number = 0
  minor: number = 0

  constructor() {

  }

  async handshake(configCallback?: (config: any) => Promise<void>) {
    write(`Started ${major}.${minor}`)
    let groups = await expect(/Version ([0-9]+)\.([0-9]+)/)
    this.major = Number(groups[1])
    this.minor = Number(groups[2])
    this.assertCompatibility()
    write('Ready')
    groups = await expect(/Config (.*)/)
    this.config = JSON.parse(groups[1])
    if (configCallback) {
      try {
        await configCallback(this.config)
      } catch (e) {
        // Send "Configured" message to the protocol first so that it'll move to the current_step
        // That way the sendError call will call error_report_fun
        write('Configured')
        this.sendError(e)
        return
      }
    }
    write('Configured')
  }

  getConfigValue(monitorLogicalName: string, propertyName: string) {
    let retVal = null
    this.logDebug(`Configure ${monitorLogicalName}.${propertyName}`)
    if (propertyName in this.config) {
      this.logDebug('- set value from runner config')
      retVal = this.config[propertyName] || null
    }
    var envName = 'CANARY_' + snakeAndUpcase(monitorLogicalName) + snakeAndUpcase(propertyName)
    var metristEnvName = 'METRIST_' + snakeAndUpcase(monitorLogicalName) + snakeAndUpcase(propertyName)
    if (envName in process.env || metristEnvName in process.env) {
      this.logDebug(`- set/override value from env var ${envName}`)
      retVal = process.env[metristEnvName] || process.env[envName] || null
    }
    return retVal
  }

  async getStep(cleanupHandler: CleanupHandler, teardownHandler: TeardownHandler) {
    const msg = await read()
    if (msg == 'Exit 0') {
      await teardownHandler()
      return null
    }
    if (msg == 'Exit 1') {
      await teardownHandler()
      await cleanupHandler()
      return null
    }
    if (msg.startsWith('Run Step')) {
      const parts = msg.split(' ')
      return parts[2]
    }
    this.logError(`Unexpected message "${msg}", assuming we have to exit`)
    return null
  }

  assertCompatibility() {
    const isCompatible = (this.major == major) && (this.minor >= minor)
    if (!isCompatible) {
      throw `Protocol incompatible. We support ${this.major}.${this.minor}, orchestration supports ${major}.${minor}`
    }
  }

  sendTime(time: number) {
    write('Step Time ' + time)
  }
  sendOK() {
    write ('Step OK')
  }
  sendError(error: any) {
    write('Step Error ' + error)
  }

  logDebug(msg: string) {
    write('Log Debug ' + msg)
  }
  logInfo(msg: string) {
    write('Log Info ' + msg)
  }
  logWarning(msg: string) {
    write('Log Warning ' + msg)
  }
  logError(msg: any) {
    write('Log Error ' + msg)
  }
}
