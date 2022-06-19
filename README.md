# Telebot
Telegram bot + EFK logging + Prometeous/Grafana monitoring

## How to run
1. Install a [Docker Engine](https://docs.docker.com/engine/install/) with docker-compose support (included by default in Docker Desktop).
2. Obtain a token for your telegram bot [here](https://t.me/botfather). Store it in the environment variable `Telebot_Telegram_Token` on your local machine.
3. Run `docker-compose up`

## How to use
- Grafana UI - [http://localhost:3000/](http://localhost:3000/)
- Kibana UI - [http://localhost:5601/](http://localhost:5601/)
- Telegram bot web API - [http://localhost:9091]. Includes [/hello](http://localhost:9091/hello) and [/metrics](http://localhost:9091/metrics) endpoints

## Used technologies
- [Docker](https://www.docker.com) and docker-compose
- [Asp.Net Core](https://docs.microsoft.com/en-us/aspnet/core/?view=aspnetcore-6.0) (inside a console application)
- [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) library for interaction with the [Telegram API](https://core.telegram.org/bots/api)
- [Fluentd](https://www.fluentd.org/) as a log sink
- [ElasticSearch](https://www.elastic.co/elasticsearch/) as a log database
- [Kibana](https://www.elastic.co/kibana/) as a control panel for the ElasticSearch and log visualisation UI
- [Prometheus](https://prometheus.io/) as a metrics pull agent and metrics database
- [Graphana](https://grafana.com/) as a metrics visualisation UI 

## How it works
* Telegram bot is built as an Asp.Net Core web API with a hosted worker. Telegram chat messaging is handled in this hosted worker in a separate thread
* Chat messages are fetched via a [long polling](https://core.telegram.org/bots/api#getupdates) technique
* When a user sends a new message in the Telegram chat, the following events occure:
  1. Message is logged into the Asp.Net Core console
  2. Message is sent from Asp.Net Core to fluentd 
  3. fluentd prints the message to a stdout
  4. fluentd writes the message to a file system
  5. fluentd sends the message to ElasticSearch
  6. ElasticSearch recieves, stores and indexes the message
  7. Asp.Net Core service increments a number of received requests
  8. Asp.Net Core service sends back the message to a Telegram chat
* Asp.Net Core exposes the `/metrics` API. It returns current runtime metrics (CPU load, heap memory usage etc) and application metrics (a total number of recieved messages in the chat)
* Prometheus periodically pulls the `/metrics` API and saves the metrics to own database