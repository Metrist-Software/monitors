"use strict";
const AWS = require("aws-sdk");

// This is a very simple lambda function that just reflects
// its invocation parameters. Note that in this way, our
// "observation" of AWS Lambda involves two trips through
// SQS, but give that Lambda is closely tied into SQS there
// is probably no way around that.

exports.handler = async (event) => {
  AWS.config.update({ region: process.env.AWS_REGION });

  const sqs = new AWS.SQS({ apiVersion: '2012-11-05' });
  const queueUrl = process.env.QUEUE_URL;

  var params = {
    MessageBody: JSON.stringify(event),
    QueueUrl: queueUrl
  };

  try {
    await sqs.sendMessage(params).promise();
  }
  catch (err) {
    // do something here
    console.error(err)
  }
  return {
    statusCode: 200,
    body: 'OK'
  };
};
