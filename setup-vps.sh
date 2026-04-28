#!/bin/bash
# JobAnalyzer - VPS İlk Kurulum Scripti
# Tek seferlik çalıştırılır. Ubuntu 22.04 / 24.04 için.

set -e

DEPLOY_PATH="/opt/jobanalyzer"
# GitHub repo URL'nizi buraya girin (Settings > Code > HTTPS clone URL)
REPO_URL="https://github.com/frknaydn3512/JobAnalyzer.git"

echo "=== Docker kuruluyor ==="
apt-get update
apt-get install -y ca-certificates curl gnupg git

install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
chmod a+r /etc/apt/keyrings/docker.gpg

echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
  | tee /etc/apt/sources.list.d/docker.list > /dev/null

apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

systemctl enable docker
systemctl start docker

echo "=== Proje dizini oluşturuluyor: $DEPLOY_PATH ==="
mkdir -p "$DEPLOY_PATH"

echo "=== Repo klonlanıyor ==="
git clone "$REPO_URL" "$DEPLOY_PATH"
cd "$DEPLOY_PATH"

echo ""
echo "==================================================================="
echo "KURULUM TAMAMLANDI"
echo "==================================================================="
echo ""
echo "Şimdi .env dosyasını oluşturmanız gerekiyor:"
echo ""
echo "  nano $DEPLOY_PATH/.env"
echo ""
echo ".env.example dosyasındaki tüm değerleri doldurun:"
echo "  - DEFAULT_CONNECTION  (Supabase bağlantı dizesi)"
echo "  - GROQ_API_KEY"
echo "  - ADZUNA_APP_ID / ADZUNA_APP_KEY"
echo "  - JOOBLE_API_KEY"
echo "  - SERPAPI_KEY"
echo "  - RAPIDAPI_KEY"
echo "  - IYZICO_API_KEY / IYZICO_SECRET_KEY / IYZICO_BASE_URL"
echo "  - IYZICO_CALLBACK_URL  (http://VPS_IP/Payment/Callback)"
echo ""
echo "Sonra başlatmak için:"
echo "  cd $DEPLOY_PATH && docker compose up -d"
echo ""
echo "Logları görmek için:"
echo "  docker compose logs -f web"
echo "==================================================================="
