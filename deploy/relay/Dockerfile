FROM node:22.19.0-alpine3.22 AS web-build
WORKDIR /src/web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
COPY protocol/ /src/protocol/
RUN npm run build

FROM golang:1.24.7-alpine3.22 AS relay-build
WORKDIR /src/relay
COPY relay/go.mod relay/go.sum* ./
RUN go mod download
COPY relay/ ./
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /out/relay ./cmd/relay

FROM alpine:3.22.1
RUN addgroup -S relay && adduser -S -G relay relay
WORKDIR /app
COPY --from=relay-build /out/relay /app/relay
COPY --from=web-build /src/web/dist /app/web
USER relay
ENV PORT=8080 WEB_ROOT=/app/web
EXPOSE 8080
ENTRYPOINT ["/app/relay"]
