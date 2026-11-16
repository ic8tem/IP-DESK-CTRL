# Homebrew: brew tap ic8tem/ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL
#           brew install --cask ip-desk-ctrl
# Questo file lo riscrive da solo la compilazione a ogni nuova versione (versione e impronte dei .dmg).
cask "ip-desk-ctrl" do
  arch arm: "arm64", intel: "x64"

  version "0.5.16"
  sha256 arm:   "4620d01c64f9ff545567b91fb1d9a6a19ea207c80a16f185c934c5583909c9d1",
         intel: "2f3230d7e72796f1dc81f26e020db53a456c4e8af84f6d4eaf93e0d05bd23aaa"

  url "https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v#{version}/IP-DESK-CTRL-#{version}-osx-#{arch}.dmg"
  name "IP-DESK-CTRL"
  desc "Desktop remoto cifrato con numero fisso di 10 cifre"
  homepage "https://github.com/ic8tem/IP-DESK-CTRL"

  # l'app si aggiorna da sola: Homebrew non la sostituisce con brew upgrade
  auto_updates true

  app "IP-DESK-CTRL.app"

  zap trash: "~/.config/IP-DESK-CTRL"
end
