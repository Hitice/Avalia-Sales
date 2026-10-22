#!/bin/sh
# O certbot renova o certificado sozinho; o Nginx relê a cada 6 horas para usar o novo.
(while :; do sleep 21600; nginx -s reload; done) &
