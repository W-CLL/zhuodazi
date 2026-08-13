param(
    [string]$HostName = "8.134.130.155",
    [string]$UserName = "root",
    [string]$IdentityFile = "$env:USERPROFILE\.ssh\id_ed25519"
)

$ErrorActionPreference = "Stop"
$repoRoot = (git rev-parse --show-toplevel).Trim()
$commit = (git -C $repoRoot rev-parse --short=12 HEAD).Trim()
$deploymentId = "$commit-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "deskpet-site-$deploymentId"
$sourceRoot = Join-Path $temporaryRoot "source"
$sourceArchive = Join-Path $temporaryRoot "source.tar"
$siteArchive = Join-Path $temporaryRoot "site.tar.gz"
$remoteArchive = "/tmp/deskpet-site-$deploymentId.tar.gz"
$target = "$UserName@$HostName"

try {
    New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
    git -C $repoRoot archive --format=tar -o $sourceArchive HEAD -- website scripts/check-release-consistency.py windows/ZhuoDazi/ZhuoDazi.csproj macos/Info.plist
    if ($LASTEXITCODE -ne 0) { throw "Unable to archive HEAD." }
    tar -xf $sourceArchive -C $sourceRoot
    if ($LASTEXITCODE -ne 0) { throw "Unable to extract source archive." }

    python (Join-Path $sourceRoot "scripts\check-release-consistency.py")
    if ($LASTEXITCODE -ne 0) { throw "Release metadata check failed." }
    $websiteRoot = Join-Path $sourceRoot "website"
    if (Get-Command npm -ErrorAction SilentlyContinue) {
        npm --prefix $websiteRoot ci
        if ($LASTEXITCODE -ne 0) { throw "Website dependency installation failed." }
        npm --prefix $websiteRoot run build
    }
    elseif (Get-Command pnpm -ErrorAction SilentlyContinue) {
        pnpm --dir $websiteRoot install --frozen-lockfile --ignore-scripts
        if ($LASTEXITCODE -ne 0) { throw "Website dependency installation failed." }
        pnpm --dir $websiteRoot run build
    }
    else {
        throw "npm or pnpm is required to build the website."
    }
    if ($LASTEXITCODE -ne 0) { throw "Website build failed." }

    $publicRoot = Join-Path $sourceRoot "website\public"
    if (-not (Test-Path (Join-Path $publicRoot "index.html"))) { throw "Website build is missing index.html." }
    if (-not (Test-Path (Join-Path $publicRoot "download\index.html"))) { throw "Website build is missing download/index.html." }
    tar -czf $siteArchive -C $publicRoot .
    if ($LASTEXITCODE -ne 0) { throw "Unable to package website build." }

    scp -i $IdentityFile -o BatchMode=yes -o ConnectTimeout=15 $siteArchive "${target}:$remoteArchive"
    if ($LASTEXITCODE -ne 0) { throw "Unable to upload website build." }

    $remoteScript = @'
set -eu
deployment_id="$1"
case "$deployment_id" in
  ""|*[!A-Za-z0-9._-]*) echo "Invalid deployment id." >&2; exit 2 ;;
esac

root=/www/wwwroot
current="$root/desktoppet.online"
next="$root/desktoppet.online.next-$deployment_id"
previous="$root/desktoppet.online.previous-$deployment_id"
archive="/tmp/deskpet-site-$deployment_id.tar.gz"

exec 9>/run/lock/deskpet-website-deploy.lock
flock -n 9 || { echo "Another website deployment is running." >&2; exit 3; }
test -d "$current"
test -f "$archive"
test ! -e "$next"
test ! -e "$previous"

cleanup() {
  rm -rf -- "$next"
  rm -f -- "$archive"
}
trap cleanup EXIT

mkdir -m 0755 "$next"
tar -xzf "$archive" -C "$next"
test -f "$next/index.html"
test -f "$next/download/index.html"
grep -q 'api/public/downloads' "$next/js/site.js"
grep -q 'data-release-version="windows/x64"' "$next/index.html"
grep -q 'data-release-version="macos/arm64"' "$next/download/index.html"

test ! -d "$current/.well-known" || cp -a "$current/.well-known" "$next/.well-known"
test ! -f "$current/.user.ini" || cp -a "$current/.user.ini" "$next/.user.ini"
chown -R www:www "$next"
nginx -t

mv "$current" "$previous"
if ! mv "$next" "$current"; then
  mv "$previous" "$current"
  exit 4
fi

trap - EXIT
rm -f -- "$archive"
printf 'DEPLOYED=%s\nPREVIOUS=%s\n' "$deployment_id" "$previous"
'@

    $remoteScript | ssh -i $IdentityFile -o BatchMode=yes -o ConnectTimeout=15 $target "timeout --foreground --signal=TERM --kill-after=10s 180s bash -s -- '$deploymentId'"
    if ($LASTEXITCODE -ne 0) { throw "Remote website deployment failed." }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
