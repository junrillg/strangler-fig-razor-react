<?php
// Native Adminer authentication remains mandatory; only the read-only lab connection is offered.
function adminer_object() {
    return new class extends Adminer\Adminer {
        function credentials(): array {
            return ['postgres', 'viewer_app', Adminer\get_password()];
        }
        function database(): ?string { return 'strangler'; }
        function login($login, $password) {
            return $login === 'viewer_app' && Adminer\DRIVER === 'pgsql'
                ? parent::login($login, $password) : false;
        }
        function loginFormField($name, $heading, $value): string {
            $fixed = ['driver' => 'pgsql', 'server' => 'postgres', 'username' => 'viewer_app', 'db' => 'strangler'];
            if (isset($fixed[$name])) {
                $value = '<input name="auth[' . $name . ']" value="' . $fixed[$name] . '" readonly>';
            }
            return parent::loginFormField($name, $heading, $value);
        }
        function verifyVersion(): bool { return false; }
    };
}
require '/var/www/html/adminer.php';
