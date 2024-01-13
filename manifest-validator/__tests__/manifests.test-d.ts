import Ajv, { ValidateFunction } from 'ajv/dist/jtd'
import { ajv_options, MonitorManifestSchemaType, PackageManifestSchemaType } from './test-utilities'
import { describe, expect, it } from 'vitest'
import { dirname, join as joinPath } from 'node:path'
import { fileURLToPath } from 'node:url'
import { readdir } from 'node:fs/promises'
import fs, { PathLike } from 'fs'
import monitorManifestSchemaJSON from '../src/monitor-manifest-schema.json'
import packageManifestSchemaJSON from '../src/package-manifest-schema.json'


const monitorsDirectoryRoot = joinPath(dirname(fileURLToPath(import.meta.url)), '../../')

const validateMonitor = new Ajv(ajv_options).compile<MonitorManifestSchemaType>(monitorManifestSchemaJSON)
const validatePackage = new Ajv(ajv_options).compile<PackageManifestSchemaType>(packageManifestSchemaJSON)

const getSubDirectories = async (path: PathLike) => {
  try {
    const contents = await readdir(path, { withFileTypes: true })
    return contents.filter((item) => {
      return item.isDirectory()
    })
  } catch (_err) {
    throw new Error(`Directory does not exist: ${path}`)
  }
}

const validateSchema = <T>(possiblePath: string, validator: ValidateFunction<T>) => {
  const thisManifest = require(possiblePath)

  const valid = validator(thisManifest)
  if(!valid) console.dir(validator.errors)
  expect(valid).toBe(true)
}


describe(`Monitor and package manifests are valid`, async () => {
    const monitorDirectories = await getSubDirectories(monitorsDirectoryRoot)
    monitorDirectories.forEach((monitor) => {
      const possiblePath = joinPath(monitorsDirectoryRoot, monitor.name, `monitor-manifest.json`)
      if (fs.existsSync(possiblePath)) {
        it.concurrent(`Checking monitor and package manifests for ${monitor.name}`, async () => {
          validateSchema<MonitorManifestSchemaType>(possiblePath, validateMonitor)

          const packageDirectories = await getSubDirectories(joinPath(monitorsDirectoryRoot, monitor.name))
          packageDirectories.forEach((pkg) => {
            const possiblePath = joinPath(monitorsDirectoryRoot, monitor.name, pkg.name, `manifest.json`)
            validateSchema<PackageManifestSchemaType>(possiblePath, validatePackage)
          })
        })
      }
    })
})
