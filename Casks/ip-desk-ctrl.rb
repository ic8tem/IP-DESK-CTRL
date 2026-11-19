# Homebrew: brew tap ic8tem/ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL
#           brew install --cask ip-desk-ctrl
# Questo file lo riscrive da solo la compilazione a ogni nuova versione (versione e impronte dei .dmg).
cask "ip-desk-ctrl" do
  arch arm: "arm64", intel: "x64"

  version "0.5.19"
  sha256 arm:   "1fa9349fda52dfb20bd3930a15ab07302fc26f22091e4daab94b10a3a401fb3f",
         intel: "4476ca2fb7171875adcf6406bfddca7e54bb0859c1941936a5956421beac814f"

  url "https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v#{version}/IP-DESK-CTRL-#{version}-osx-#{arch}.dmg"
  name "IP-DESK-CTRL"
  desc "Desktop remoto cifrato con numero fisso di 10 cifre"
  homepage "https://github.com/ic8tem/IP-DESK-CTRL"

  # l'app si aggiorna da sola: Homebrew non la sostituisce con brew upgrade
  auto_updates true

  app "IP-DESK-CTRL.app"

  zap trash: "~/.config/IP-DESK-CTRL"
end
