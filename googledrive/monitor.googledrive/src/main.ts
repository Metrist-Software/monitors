import { Protocol, StepFunction, timeAndSend, timedWithRetries } from '@metrist/protocol'
import { google, drive_v3 } from 'googleapis'

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}
const retryMarker = 'RETRY'

const config: any = {
  accountEmail: null,
  accountPrivateKey: null
}

const configCallback = async (_config:any) => {
  config.accountEmail = proto.getConfigValue('googledrive', 'GoogleDriveAccountEmail')
  config.accountPrivateKey = proto.getConfigValue('googledrive', 'GoogleDriveAccountPrivateKey')
}

let client: drive_v3.Drive;
let fileId: string;

const setup = async () => {
  const scopes = [
    'https://www.googleapis.com/auth/drive'
  ];
  const auth = new google.auth.JWT(
    config.accountEmail, undefined,
    config.accountPrivateKey,
    scopes);
  client = google.drive({ version: "v3", auth });
}

steps.CreateDocsFile = async () => {
  let currentRetryAttempt = 0;
  const maxRetry = 5;
  const result = await timedWithRetries(async () => {
    const metadata = {
      // if you wish to see the documents on your drive, you can share a folder
      // with {config.accountEmail} and put its ID here
      // parents: [FolderId],
      mimeType: 'application/vnd.google-apps.document'
    };

    const res = await client.files.create({
      requestBody: metadata
    });

    if(res.status == 429 && currentRetryAttempt < maxRetry) {
      const delaySeconds = (10 * (currentRetryAttempt + 1)) + getRandomIntInclusive(-5, 5)
      proto.logInfo("Received status code: 429. Retrying after " + delaySeconds + " seconds")
      await new Promise(resolve => {
        setTimeout(resolve, delaySeconds * 1000);
      });
      currentRetryAttempt += 1
      throw retryMarker;
    }

    if (!res?.data?.id) {
      throw new Error("CreateDocsFile did not create a file id");
    }
    fileId = res.data.id;
    proto.logInfo(`${res.status}: ${res.statusText}`);
  },
  (ex) => ex == retryMarker,
  0 // Sleeping is handled by the delay above
  );

  proto.sendTime(result.successTime);
}

function getRandomIntInclusive(min: number, max: number) {
  min = Math.ceil(min);
  max = Math.floor(max);
  return Math.floor(Math.random() * (max - min + 1) + min);
}

steps.GetDocsFile = async () => {
  await timeAndSend(proto, async () => {
    const res = await client.files.get({
      fileId: fileId
    });
    proto.logInfo(`${res.status}: ${res.statusText}`);
  });
}

steps.DeleteDocsFile = async () => {
  await timeAndSend(proto, async () => {
    const res = await client.files.delete({
      fileId: fileId
    });
    proto.logInfo(`${res.status}: ${res.statusText}`);
  });
}

const teardownHandler = async function() {}

const cleanupHandler = async function () {
  proto.logInfo(`Performing Google Drive file cleanup for account ${config.accountEmail}`);

  const tenMinutesBefore = new Date(Date.now() - 10*60*1000);

  // there should be no files owned by {config.accountEmail} after all steps finish
  const query = `"${config.accountEmail}" in owners and createdTime < "${tenMinutesBefore.toISOString()}"`;
  const res = await client.files.list({
    fields: 'files(id)',
    q: query
  });
  const files = res?.data?.files;
  if (!files) return;
  for (const file of files) {
    if (!file.id) continue;
    // CAREFUL - this will delete ANY files owned by config.accountEmail
    proto.logInfo(`Found orphan file: ${file.id}. Attempting to delete...`);
    const res = await client.files.delete({
      fileId: file.id
    });
    proto.logInfo(`${res.status}: ${res.statusText}`);
  }
}

async function main() {
  await proto.handshake(configCallback);
  await setup();
  let step: string | null = null
  while((step = await proto.getStep(cleanupHandler, teardownHandler)) != null) {
    proto.logDebug(`Starting step ${step}`)
    await steps[step]()
      .catch(err => proto.sendError(err))
  }
  proto.logDebug('Orchestrator asked me to exit, all done');
  process.exit(0);
}

main();
