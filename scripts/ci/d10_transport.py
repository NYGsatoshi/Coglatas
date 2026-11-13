"""Reuse the fixed-origin SEC-ARCH client; add bounded, credential-separated ZIP download."""
import urllib.error
import urllib.parse
import urllib.request

from sec_arch_github_provenance import LiveGitHub

MAX_ZIP = 16 * 1024 * 1024


class DownloadRefused(ValueError):
    pass


class StopRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None


def storage_location(value):
    if type(value) is not str or not 0 < len(value) <= 8192:
        raise DownloadRefused('DOWNLOAD_REFUSED')
    try:
        parsed = urllib.parse.urlsplit(value)
        valid = (parsed.scheme == 'https' and parsed.hostname and
                 any(parsed.hostname.endswith(suffix) for suffix in ['.blob.core.windows.net', '.githubusercontent.com'])
                 and parsed.port in [None, 443] and parsed.username is None and parsed.password is None
                 and not parsed.fragment and parsed.path and not any(ord(c) < 32 for c in value))
    except ValueError:
        valid = False
    if not valid: raise DownloadRefused('DOWNLOAD_REFUSED')
    return value


class ArtifactGitHub(LiveGitHub):
    def download(self, artifact_id):
        if type(artifact_id) is not int or artifact_id <= 0:
            raise DownloadRefused('DOWNLOAD_REFUSED')
        headers = {'Accept': 'application/vnd.github+json', 'User-Agent': 'Coglatas-D10/1',
                   'X-GitHub-Api-Version': '2022-11-28'}
        if self.token: headers['Authorization'] = 'Bearer ' + self.token
        request = urllib.request.Request(f'https://api.github.com/repos/NYGsatoshi/Coglatas/actions/artifacts/{artifact_id}/zip', headers=headers)
        opener = urllib.request.build_opener(StopRedirect())
        try:
            with opener.open(request, timeout=30): pass
        except urllib.error.HTTPError as error:
            if error.code != 302: raise DownloadRefused('DOWNLOAD_REFUSED') from None
            location = storage_location(error.headers.get('Location'))
        else:
            raise DownloadRefused('DOWNLOAD_REFUSED')
        # Fresh request, no API credentials; reject any second redirect.
        with opener.open(urllib.request.Request(location, headers={'User-Agent': 'Coglatas-D10/1'}), timeout=30) as response:
            if response.status != 200: raise DownloadRefused('DOWNLOAD_REFUSED')
            data = response.read(MAX_ZIP + 1)
        if not 0 < len(data) <= MAX_ZIP: raise DownloadRefused('DOWNLOAD_REFUSED')
        return data
