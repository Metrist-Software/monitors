const express = require('express')

const app = express()

app.get('/', (req, res) => {
  res.status(200).send(process.env.GAE_VERSION).end()
})

const PORT = parseInt(process.env.PORT) || 8080
app.listen(PORT, () => {
  console.log(`App listening on port ${PORT}`)
})

module.exports = app
