FROM adminer:6.1.2-standalone@sha256:251295c1cd51d3889f1c42ffa4864b24eeab51f0caf395cfe5bf4726c5ec9a2a
USER root
RUN apk add --no-cache --upgrade brotli-libs=1.2.1-r0
# The official image has not yet published 6.2.0; use the exact upstream release asset and its published SHA256.
ADD --checksum=sha256:9047c00dfc8258c6046c6720d7d201eaa0ca016fa0fc554bf0c4c137306e1c16 --chown=adminer:adminer https://github.com/vrana/adminer/releases/download/v6.2.0/adminer-6.2.0.php /var/www/html/adminer.php
COPY --chown=adminer:adminer infrastructure/adminer/index.php /srv/viewer/index.php
COPY --chown=adminer:adminer infrastructure/adminer/router.php /opt/viewer-router.php
RUN php -l /srv/viewer/index.php \
    && php -l /opt/viewer-router.php \
    && printf 'display_errors=Off\nlog_errors=On\nexpose_php=Off\nmemory_limit=64M\nmax_execution_time=30\nsession.cookie_httponly=On\nsession.cookie_samesite=Strict\n' > /usr/local/etc/php/conf.d/lab.ini
ENV ADMINER_VERSION=6.2.0
USER adminer
WORKDIR /srv/viewer
ENTRYPOINT ["docker-php-entrypoint"]
CMD ["php", "-S", "0.0.0.0:8080", "-t", "/srv/viewer", "/opt/viewer-router.php"]
HEALTHCHECK --interval=10s --timeout=5s --start-period=10s --retries=12 CMD php -r '$response=file_get_contents("http://127.0.0.1:8080/"); exit($response !== false && str_contains($response, "auth[password]") ? 0 : 1);'
