FROM node:alpine

WORKDIR /app

COPY package.json package-lock.json app.js /app/

RUN npm install

CMD ["node", "app.js"]
