# Homebrew: brew tap ic8tem/ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL
#           brew install --cask ip-desk-ctrl
# Questo file lo riscrive da solo la compilazione a ogni nuova versione (versione e impronte dei .dmg).
cask "ip-desk-ctrl" do
  arch arm: "arm64", intel: "x64"

  version "0.5.35"
  sha256 arm:   "71341f6344ea60085c64e20a8ac7f899fe7d7962467d4ab2c798b28720dcdbfc",
         intel: "ecfa81dfe7f350267d602cb87e47a09cfec74433d70efb84d91c88b0a3de0f42"

  url "https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v#{version}/IP-DESK-CTRL-#{version}-osx-#{arch}.dmg"
  name "IP-DESK-CTRL"
  desc "Desktop remoto cifrato con numero fisso di 10 cifre"
  homepage "https://github.com/ic8tem/IP-DESK-CTRL"

  # l'app si aggiorna da sola: Homebrew non la sostituisce con brew upgrade
  auto_updates true

  app "IP-DESK-CTRL.app"

  zap trash: "~/.config/IP-DESK-CTRL"
end
