# O front é compilado aqui e servido pelo mesmo Nginx que leva /api, /q/ e /webhooks/ à API.
FROM node:24-alpine AS front
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --ignore-scripts
COPY web/ ./
ARG VITE_SENTRY_DSN=""
ENV VITE_SENTRY_DSN=$VITE_SENTRY_DSN
ARG VITE_SENTRY_RELEASE=""
ENV VITE_SENTRY_RELEASE=$VITE_SENTRY_RELEASE
RUN npm run build

FROM nginx:1.28-alpine
COPY infra/nginx/avalia-sales.conf.template /etc/nginx/templates/avalia-sales.conf.template
COPY infra/nginx/certificado-provisorio.sh /docker-entrypoint.d/05-certificado-provisorio.sh
COPY infra/nginx/recarregar.sh /docker-entrypoint.d/99-recarregar.sh
RUN chmod +x /docker-entrypoint.d/05-certificado-provisorio.sh /docker-entrypoint.d/99-recarregar.sh \
 && rm -f /etc/nginx/conf.d/default.conf
COPY --from=front /web/dist /usr/share/nginx/html
