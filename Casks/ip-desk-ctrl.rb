# Homebrew: brew tap ic8tem/ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL
#           brew install --cask ip-desk-ctrl
# Questo file lo riscrive da solo la compilazione a ogni nuova versione (versione e impronte dei .dmg).
cask "ip-desk-ctrl" do
  arch arm: "arm64", intel: "x64"

  version "0.5.22"
  sha256 arm:   "0311cb252975ac475971b3bcd1839150cbb44dc0f5171a4eda29159f83532381",
         intel: "dd93683195736cfd3ee26d005babce3ee789d177bd93c7af8e30b034908a04b2"

  url "https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v#{version}/IP-DESK-CTRL-#{version}-osx-#{arch}.dmg"
  name "IP-DESK-CTRL"
  desc "Desktop remoto cifrato con numero fisso di 10 cifre"
  homepage "https://github.com/ic8tem/IP-DESK-CTRL"

  # l'app si aggiorna da sola: Homebrew non la sostituisce con brew upgrade
  auto_updates true

  app "IP-DESK-CTRL.app"

  zap trash: "~/.config/IP-DESK-CTRL"
end
