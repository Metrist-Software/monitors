const express = require('express')
const app = express()

app.get('/', function (req, res) {
  res.send(process.env.HOSTNAME)
})

app.listen(3000)
