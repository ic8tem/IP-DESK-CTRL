# Homebrew: brew tap ic8tem/ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL
#           brew install --cask ip-desk-ctrl
# Questo file lo riscrive da solo la compilazione a ogni nuova versione (versione e impronte dei .dmg).
cask "ip-desk-ctrl" do
  arch arm: "arm64", intel: "x64"

  version "0.5.26"
  sha256 arm:   "56f21b897da957b23d73a0530f14edda91684e804a8b74e72aaf93ec43385004",
         intel: "4a3fec307600ab7e13c4302b62ef60b91659e5ecd7a5f177ffa16a6c0ddee03b"

  url "https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v#{version}/IP-DESK-CTRL-#{version}-osx-#{arch}.dmg"
  name "IP-DESK-CTRL"
  desc "Desktop remoto cifrato con numero fisso di 10 cifre"
  homepage "https://github.com/ic8tem/IP-DESK-CTRL"

  # l'app si aggiorna da sola: Homebrew non la sostituisce con brew upgrade
  auto_updates true

  app "IP-DESK-CTRL.app"

  zap trash: "~/.config/IP-DESK-CTRL"
end
