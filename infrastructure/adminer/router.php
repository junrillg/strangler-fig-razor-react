<?php
$path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
if ($path !== '/' && $path !== '/index.php') {
    http_response_code(404);
    echo 'Not found';
    return;
}
require '/srv/viewer/index.php';
