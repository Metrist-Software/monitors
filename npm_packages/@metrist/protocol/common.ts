import path from 'path'
import os from 'os'
import { performance } from 'perf_hooks'
import { Protocol, StepFunction, CleanupHandler, TeardownHandler } from './protocol'


async function emptyAsyncFn() { }
export async function run(
  proto: Protocol,
  steps: Record<string, StepFunction>,
  cleanupHandler: CleanupHandler = emptyAsyncFn,
  teardownHandler: TeardownHandler = emptyAsyncFn) {
  await proto.handshake()
  let step: string | null = null
  while ((step = await proto.getStep(cleanupHandler, teardownHandler)) != null) {
    proto.logDebug(`Starting step ${step}`)
    try {
      await steps[step]()
    } catch (err) {
      proto.sendError(err)
    }
  }
  proto.logInfo('Orchestrator asked me to exit, all done')
  process.exit(0)
}

export interface Monitor {
  setConfigs: (configs: any) => Promise<void>;
  cleanupHandler: CleanupHandler;
  teardownHandler: TeardownHandler;
  steps: Record<string, StepFunction>;
}

export function timedS(funToTime: () => void) {
  const startTime = performance.now()
  funToTime()
  const endTime = performance.now()
  return endTime - startTime
}

export async function timed(funToTime: () => Promise<any>) {
  const startTime = performance.now()
  await funToTime()
  const endTime = performance.now()
  return endTime - startTime
}

export async function timedWithReturn<T>(funToTime: () => Promise<T>) {
  const startTime = performance.now()
  const value = await funToTime()

  const endTime = performance.now()
  return {
    time: endTime - startTime,
    value
  }
}

export async function timedWithRetries(fncToTime: () => Promise<void>, shouldRetry: (ex: any) => boolean, sleepTimeout = 1000) {
  const startTime = performance.now()
  while (true) {
    try {
      const successTime = await timed(fncToTime)
      const endTime = performance.now()

      return {
        totalTime: endTime - startTime,
        successTime
      }
    } catch (err: any) {
      if (shouldRetry(err)) {
        await new Promise((resolve) => setTimeout(resolve, sleepTimeout))
      } else {
        throw err
      }
    }
  }
}

export async function timeAndSend(protocol: Protocol, fn: () => Promise<void>) {
  return timed(fn).then(protocol.sendTime);
}

export function tmpDir(monitorLogicalName: string, ...paths: string[]) {
  return path.join(os.tmpdir(), monitorLogicalName, ...paths)
}
