export const ajv_options: object = {
  allErrors: true,
  code: { esm: true },
  strict: true,
  strictRequired: true,
  strictTuples: true,
  strictTypes: true,
  verbose: true
}

export interface MonitorManifestSchemaType {
  description?: string,
  logical_name: string,
  name: string,
  homepage: string,
  status_page_url: string,
  groups: string[]
}


export interface PackageManifestSchemaType {
  contributors?: string[],
  description?: string,
  environment_variables?: object[],
  has_cleanup_tasks: boolean,
  homepage?: string,
  license?: string,
  logical_name: string,
  package_name: string,
  publisher: string,
  repository: string,
  runtime_type: string[],
  steps: object[],
  tags?: string[],
  version: string
}

