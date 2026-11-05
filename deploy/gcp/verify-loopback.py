#!/usr/bin/env python3
"""Admit only rendered, loopback-published development Compose configurations."""

import ipaddress
import json
import sys


def admit(configuration):
    networks = configuration.get("networks", {})
    if not isinstance(networks, dict):
        raise ValueError("networks")
    for network in networks.values():
        # Routed bridges, trusted host interfaces and external networks can
        # expose container addresses even when a host port names loopback.
        if (not isinstance(network, dict) or network.get("external", False)
                or network.get("driver", "bridge") != "bridge"
                or network.get("driver_opts", {})):
            raise ValueError("network-options")
    services = configuration.get("services")
    if not isinstance(services, dict) or not isinstance(services.get("app"), dict):
        raise ValueError("services")
    app_port = False
    for name, service in services.items():
        if not isinstance(service, dict) or "network_mode" in service:
            raise ValueError("network")
        ports = service.get("ports", [])
        if not isinstance(ports, list):
            raise ValueError("ports")
        for port in ports:
            if not isinstance(port, dict):
                raise ValueError("port")
            address = port.get("host_ip")
            if not isinstance(address, str) or not ipaddress.ip_address(address).is_loopback:
                raise ValueError("binding")
            published = str(port.get("published", ""))
            if not published.isascii() or not published.isdecimal() or not 1 <= int(published) <= 65535:
                raise ValueError("published")
            if name == "app" and port.get("target") == 8080 and port.get("protocol", "tcp") == "tcp":
                app_port = True
    if not app_port:
        raise ValueError("app-port")


def main():
    try:
        # Rendered Compose includes secrets. Never echo input or parser exceptions.
        document = sys.stdin.read(1024 * 1024 + 1)
        if len(document) > 1024 * 1024:
            raise ValueError("size")
        configuration = json.loads(document)
        if not isinstance(configuration, dict):
            raise ValueError("configuration")
        admit(configuration)
    except (ValueError, TypeError, RecursionError):
        print("GCP development admission denied: require explicit loopback-only published ports; use COGLATAS_PORT=127.0.0.1:8080 and an SSH tunnel. No lifecycle command was admitted.", file=sys.stderr)
        return 1
    print("GCP development loopback admission passed; public HTTPS and Pilot admission remain separate approvals.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
