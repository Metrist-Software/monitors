import { InstancesClient, ZoneOperationsClient } from "@google-cloud/compute";
import { Protocol, StepFunction, run, timeAndSend } from '@metrist/protocol'
import { v4 as uuidV4 } from "uuid";
import { google } from "@google-cloud/compute/build/protos/protos";

const proto = new Protocol();
const steps: Record<string, StepFunction> = {};
const machineType = "e2-micro";
const sourceImage = "projects/debian-cloud/global/images/family/debian-10";
const networkName = "global/networks/default"

let instancesClient: InstancesClient;
let zoneOperationsClient: ZoneOperationsClient;
let instanceName: string;
let isConfigured = false;
let instanceDeletionSuccessful = false;

let env: string;
let gcpZone: string;

const config = {
    gcpProjectId: '',
    gcpPrivateKey: ''
};

function ensureNotNullOrUndefined<T>(arg: T | null | undefined): T {
    if (arg == null) {
        throw new Error("Must not be null");
    }

    return arg;
}

function configureIfNeeded() {
    if (isConfigured) {
        return;
    }
    config.gcpProjectId = ensureNotNullOrUndefined(proto.getConfigValue('gcpcomputeengine', 'ProjectId'));
    const privateKey = ensureNotNullOrUndefined(proto.getConfigValue('gcpcomputeengine', 'PrivateKey'));
    if (privateKey) {
        config.gcpPrivateKey = Buffer.from(privateKey, 'base64').toString();
    }
    env = ensureNotNullOrUndefined(proto.getConfigValue('gcpcomputeengine', 'EnvironmentTag') ?? process.env['ENVIRONMENT_TAG']);
    gcpZone = ensureNotNullOrUndefined(proto.getConfigValue('gcpcomputeengine', 'Region') ?? process.env['GCP_ZONE']);
    const credentials = JSON.parse(config.gcpPrivateKey)
    instancesClient = new InstancesClient({ credentials: credentials });
    zoneOperationsClient = new ZoneOperationsClient({ credentials: credentials });
    instanceName = `gcemonitor-${env}-${uuidV4()}`
    isConfigured = true;
}

function ensureConfiguredAndTimeCallback(callback: () => Promise<void>) {
    configureIfNeeded();
    return timeAndSend(proto, callback);
}

async function createInstance() {
    const { gcpProjectId } = config;
    const [response] = await instancesClient.insert({
        instanceResource: {
            name: instanceName,
            disks: [
                {
                    initializeParams: {
                        diskSizeGb: '10',
                        sourceImage,
                    },
                    autoDelete: true,
                    boot: true,
                    type: 'PERSISTENT',
                },
            ],
            machineType: `zones/${gcpZone}/machineTypes/${machineType}`,
            networkInterfaces: [
                {
                    name: networkName,
                },
            ],
        },
        project: gcpProjectId,
        zone: gcpZone,
    });

    if (response.error) {
        throw response.error;
    }

    let operation: any = response.latestResponse;

    // Wait for the create operation to complete.
    while (operation.status !== 'DONE') {
        [operation] = await zoneOperationsClient.wait({
            operation: operation.name,
            project: gcpProjectId,
            zone: operation.zone.split('/').pop(),
        });

        checkAndRaiseOnError(operation);
    }

}

async function getInstanceInfo() {
    await instancesClient.get({
        instance: instanceName,
        project: config.gcpProjectId,
        zone: gcpZone,
    });
}

async function deleteInstance() {
    const { gcpProjectId } = config;
    const [response] = await instancesClient.delete({
        project: gcpProjectId,
        zone: gcpZone,
        instance: instanceName,
    });

    if (response.error) {
        throw response.error;
    }

    let operation: any = response.latestResponse;

    // Wait for the delete operation to complete.
    while (operation.status !== 'DONE') {
        [operation] = await zoneOperationsClient.wait({
            operation: operation.name,
            project: gcpProjectId,
            zone: operation.zone.split('/').pop(),
        });

        checkAndRaiseOnError(operation);
    }

    instanceDeletionSuccessful = true;
}

function checkAndRaiseOnError(operationResult: google.cloud.compute.v1.IOperation) {
    if(operationResult.error) {
        const errorMessage = operationResult.error.errors?.map(e => e.message).join("\n");
        throw errorMessage;
    }
}

steps.CreateInstance = function () {
    return ensureConfiguredAndTimeCallback(async () => createInstance());
}

steps.GetInstanceInfo = function () {
    return ensureConfiguredAndTimeCallback(async () => getInstanceInfo());
}

steps.DeleteInstance = function () {
    return ensureConfiguredAndTimeCallback(async () => deleteInstance());
}

async function cleanupHandler() {
    configureIfNeeded()
    const { gcpProjectId } = config;

    // example name: gcemonitor-dev1-f8055e49-48ea-4f56-bd78-b58951ed71a2
    const filter = `name eq gcemonitor-${env}-.*`
    const [instancesList] = await instancesClient.list({
        project: gcpProjectId,
        zone: gcpZone,
        filter
    })
    for (const instance of instancesList) {
        if (!instance?.creationTimestamp) continue

        const curr_date = new Date(instance.creationTimestamp)
        const tenMinutesBefore = new Date(Date.now() - 10*60*1000);
        if (curr_date < tenMinutesBefore) {
            await instancesClient.delete({
                project: config.gcpProjectId,
                zone: gcpZone,
                instance: instance.name,
            })
        }
    }
}

async function teardownHandler() {
  if (instanceDeletionSuccessful) {
    return;
  }
  try {
      await instancesClient.delete({
          project: config.gcpProjectId,
          zone: gcpZone,
          instance: instanceName,
      });
  } catch (e) {
      proto.logError(e)
  }
}

async function main() {
    await run(proto, steps, cleanupHandler, teardownHandler);
}

main();
