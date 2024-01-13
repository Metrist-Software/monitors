import { randomUUID } from 'crypto'
import { Protocol, StepFunction, timed, timedWithRetries } from '@metrist/protocol'
import { axios } from '@metrist/axios_proxy'

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}
const uuid: string = randomUUID()
const baseUrl = 'https://console.neon.tech/api/v2'
const branchPrefix = 'metrist-'

let branchId : string = ''

class RetryException {}

const config: any = {
  apiKey: null,
  projectId: null,
  baseBranchId: null
}

const configCallback = async (_config: any) => {
  config.apiKey = proto.getConfigValue('neon', 'ApiKey')
  config.projectId = proto.getConfigValue('neon', 'ProjectId')
  config.baseBranchId = proto.getConfigValue('neon', 'BaseBranchId')
}

steps.CreateBranch = async () => {
  const url = `${baseUrl}/projects/${config.projectId}/branches`
  const data = {
    branch: {
      name: `${branchPrefix}${uuid}`,
      parent_id: config.baseBranchId
    }
  }
  proto.logInfo(`Creating branch at ${url}`)
  const time = await timed(async () => {
    const response = await axios.post(
      url,
      data,
      {
        headers: {
          'authorization': `Bearer ${config.apiKey}`,
          'accept': 'application/json',
          'content-type': 'application/json'
        },
        validateStatus: (_status) => true
      },
    )

    if (response.status != 201) {
      proto.logError(`Unexpected response: ${response.status}/${JSON.stringify(response.data)}`)
      throw `Unexpected response ${response.status}`
    }

    branchId = response.data.branch.id
    proto.logInfo(`Branch created with id ${branchId} and name ${response.data.branch.name}`)
  })
  proto.logInfo(`CreateBranch completed in ${time}ms`)
  proto.sendTime(time)
}

steps.DeleteBranch = async () => {
  const doDeleteBranch = async () => {
    try {
      const time = await timed(async () => {
        const response = await deleteFunction(branchId)

        if (response.status == 423) {
          proto.logInfo(`Got 423, will retry in 5 seconds`)
          throw new RetryException
        }
        if (response.status != 200) {
          proto.logError(`Unexpected response: ${response.status}/${JSON.stringify(response.data)}`)
          throw `Unexpected response ${response.status}`
        }
      })
      proto.logInfo(`DeleteBranch completed in ${time}ms`)
      proto.sendTime(time)
      return true
    } catch (ex : any) {
      if (ex instanceof RetryException) {
        return false
      } else {
        throw ex
      }
    }
  }

  for (let i = 0; i < 10; i++) {
    if (await doDeleteBranch()) {
      return
    }
    else {
      await new Promise(r => setTimeout(r, 5000))
    }
  }
  throw "Retried delete maximum times, giving up."
}

const cleanupHandler = async () => {
  const url = `${baseUrl}/projects/${config.projectId}/branches`

  const response = await axios.get(
    url,
    {
      headers: {
        'authorization': `Bearer ${config.apiKey}`,
        'content-type': 'application/json'
      },
      validateStatus: (_status) => true
    },
  )

  const deletePromises = response.data.branches.filter((branch : any) => {
    const createdAt = new Date(branch.created_at)
    const thirtyMinutesAgo = new Date(new Date().getTime() - (30 * 60000))
    return branch.name.startsWith(branchPrefix) && createdAt.getTime() < thirtyMinutesAgo.getTime()
  }).map(({ id }: { id: string }) => {
    return deleteFunction(id)
  })

  if (deletePromises.length == 0) {
    proto.logInfo(`Cleanup skipped. No stale monitors`)
    return
  }

  const results = await Promise.all(deletePromises)
  proto.logInfo(`Cleanup done. Deleted ${deletePromises.length} synthetic monitors`)
}

const deleteFunction = async (id:string) => {
  const url = `${baseUrl}/projects/${config.projectId}/branches/${id}`
  proto.logInfo(`Deleting branch with id ${id}`)
  return axios.delete(
    url,
    {
      headers: {
        'authorization': `Bearer ${config.apiKey}`,
        'content-type': 'application/json'
      },
      validateStatus: (_status) => true
    }
  )
}

async function main() {
  await proto.handshake(configCallback)
  let step: string | null = null
  while ((step = await proto.getStep(
    async () => { },
    cleanupHandler,
  )) != null) {
    proto.logDebug(`Starting step ${step}`)
    await steps[step]()
      .catch(err => proto.sendError(err))
  }
  proto.logDebug('Orchestrator asked me to exit, all done')
  process.exit(0)
}

main()
