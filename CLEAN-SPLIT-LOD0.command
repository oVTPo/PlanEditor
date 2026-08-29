#!/bin/zsh
set -e

LOD0="PlanEditor.App/MapData/Satellite/LOD0"

mkdir -p "$LOD0"

rm -f "$LOD0/01-northwest.mbtiles"
rm -f "$LOD0/02-northeast-sea.mbtiles"
rm -f "$LOD0/03-central-west.mbtiles"
rm -f "$LOD0/04-central-east-sea.mbtiles"
rm -f "$LOD0/05-southwest.mbtiles"
rm -f "$LOD0/06-southeast-sea.mbtiles"

echo "Đã xóa 6 MBTiles LOD0 chia nhỏ cũ."
echo "Giữ lại vietnam-eastsea.mbtiles nếu đang có."
