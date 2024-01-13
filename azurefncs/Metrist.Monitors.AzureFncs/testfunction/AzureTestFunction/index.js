module.exports = async function (context, req) {
    const id = (req.query.id || (req.body && req.body.id));
    const responseMessage = id
        ? id
        : "This HTTP triggered function executed successfully. Pass an ID in the query string or request body";

    context.res = {
        status: 200,
        body: responseMessage
    };
}
