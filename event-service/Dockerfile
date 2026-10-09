FROM python:3.10-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app.py schemas.py ./
COPY resources ./resources

EXPOSE 5000

CMD ["flask", "run", "--host", "0.0.0.0"]
