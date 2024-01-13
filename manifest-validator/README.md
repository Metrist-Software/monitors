# Manifest Publication

* [assets.metrist.io/dist/monitors/manifests.json](https://assets.metrist.io/dist/monitors/manifests.json)

Monitors, like any source code, have natural attributes such as author, contributors, repository url, or license. Monitors also have attributes as a consequence of the design and behaviour of Metrist’s Orchestrator, backend, and protocol such as steps, environment variables, or optional cleanup tasks.

Those metadata are documented in a file, `monitor-manifest.json`, stored in the root of each monitor’s workspace. Those data are collected by [package.sh](../deploy/package.sh) and uploaded to S3, [assets.metrist.io/dist/monitors/manifests.json](https://assets.metrist.io/dist/monitors/manifests.json), to be consumed by CLI, 3rd parties, or docs.metrist.io.

## Manifest Validation

GitHub actions (e.g., on Pull Request) run `npm run test` in the root of this project.

The test suite does two things:

1. Check that the schema’s design passes all tests.
1. Check that all `monitor-manifest.json` files in each monitor’s directory is valid according to said schema.

## Manifest Schema

In principle, this schema is simple yet expressive. In this workspace resides [the schema](src/schema.json) (and associated tests) used to validate monitor manifests.

This standard (i.e., schema) is published so Metrist and/or its users can describing a monitor’s metadata (e.g., name, functionality, author, license, _etcetera_) in a standard way. These data are vital to both Metrist and to consumers/users of the monitors.

### Helpful Links about JTD

* [RFC 8927: JSON Type Definition](https://www.rfc-editor.org/rfc/rfc8927)
* [AJV: JSON Type Definition](https://ajv.js.org/json-type-definition.html)
* [Learn JSON Typedef in 5 Minutes](https://jsontypedef.com/docs/jtd-in-5-minutes/#elements-schemas)
