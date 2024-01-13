FROM python:3.9-buster

WORKDIR /app

ENV OUTPUT_NAME=monitor

COPY ./requirements.txt /app/requirements.txt
RUN pip install -r requirements.txt

ENTRYPOINT pyinstaller monitor.py -F -n ${OUTPUT_NAME}
