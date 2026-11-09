"""Draw a planning artifact; this does not build or modify a Unity scene."""
from pathlib import Path
import json
import math
import sys
ROOT = Path(__file__).resolve().parents[1]
LOCAL_LIB = ROOT / "VR-Motor-Street-Riding/Personal/PlanningPython"
if LOCAL_LIB.exists():
    sys.path.insert(0,str(LOCAL_LIB))
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.font_manager import FontProperties
from matplotlib.patches import Rectangle
from matplotlib.lines import Line2D

OUT = ROOT / "Documents/Simulator"
FONT = FontProperties(fname=r"C:\Windows\Fonts\msjh.ttc")
RAW = [(-800,-640),(-200,-640),(-200,-240),(320,-240),(320,-640),
       (800,-640),(800,40),(480,40),(480,480),(800,480),(800,800),
       (80,800),(80,480),(-400,480),(-400,800),(-800,800),(-800,160),
       (-480,160),(-480,-160),(-800,-160),(-800,-640)]
points = [(x*.8,z*.8) for x,z in RAW]
length = sum(math.dist(a,b) for a,b in zip(points,points[1:]))
directions = []
for i in range(1,len(points)-1):
    a,b,c=points[i-1:i+2]
    incoming=(b[0]-a[0],b[1]-a[1]);outgoing=(c[0]-b[0],c[1]-b[1])
    assert incoming[0]*outgoing[0]+incoming[1]*outgoing[1] == 0
    cross=incoming[0]*outgoing[1]-incoming[1]*outgoing[0]
    directions.append("left" if cross>0 else "right")
assert points[0] == points[-1]
assert all(-1024<x<1024 and -1024<z<1024 for x,z in points)

data={
    "schema":"ait-route-concept-v0", "updated":"2026-11-07",
    "status":"planning_only_not_a_drivable_route_or_lane_graph",
    "coordinate_system":"metres, x east, y up, z north; fictional city",
    "world_bounds_xz_m":[-1024,-1024,1024,1024],
    "nominal_chunk_size_m":256, "chunk_grid":[8,8],
    "route_id":"concept_A", "closed":True,
    "planar_polyline_length_m":round(length,3),
    "internal_turn_counts":{"left":directions.count("left"),"right":directions.count("right")},
    "geometry_note":"Sharp corner planning polyline only. Recompute after lane offsets and turn curves. Branch roads, lane IDs and event windows are not authored.",
    "points":[{"id":"START" if i==0 else "END" if i==len(points)-1 else f"D{i:02d}",
               "position_m":[x,0,z],
               "turn":"start" if i==0 else "finish" if i==len(points)-1 else directions[i-1]}
              for i,(x,z) in enumerate(points)],
    "candidate_tasks":[
        {"type":"navigation","anchor":"D02","status":"candidate; requires a meaningful branch choice"},
        {"type":"lane_change","segment":["D05","D06"],"status":"candidate; requires same-direction multiple lanes and gap scenarios"},
        {"type":"obstacle_ahead","segment":["D12","D13"],"status":"candidate; sight distance, traffic and event windows remain to be designed"}
    ]
}
(OUT/"CityRouteConcept.json").write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")

plt.rcParams["svg.fonttype"]="path"
fig=plt.figure(figsize=(13,10),facecolor="#f8fafc")
ax=fig.add_axes([.075,.15,.68,.76]);ax.set_aspect("equal")
ax.set_facecolor("#ffffff")
zones=[(-960,-960,"#eaf1fb","起終點／住宅"),(0,-960,"#fff1d8","幹道／配送街區"),
       (0,0,"#e8f4ec","商圈／多車道"),(-960,0,"#f0eafb","住宅／開闊路段")]
for x,z,col,label in zones:
    ax.add_patch(Rectangle((x,z),960,960,facecolor=col,edgecolor="none",zorder=0))
    label_z=z+110 if z<0 else z+875
    ax.text(x+480,label_z,label,fontproperties=FONT,fontsize=11,color="#64748b",ha="center",zorder=1)

ticks=list(range(-1024,1025,256))
for v in ticks:
    ax.axvline(v,color="#cbd5e1",lw=.7,ls="--",zorder=1)
    ax.axhline(v,color="#cbd5e1",lw=.7,ls="--",zorder=1)
ax.add_patch(Rectangle((-1024,-1024),2048,2048,fill=False,edgecolor="#334155",lw=1.5,zorder=2))
# Example player is at D06, in cell (6,4); this is a base working set, not a cap.
ax.add_patch(Rectangle((256,-256),768,768,facecolor="#d5e8ff",alpha=.4,
                       edgecolor="#3b82f6",hatch="///",lw=1.4,zorder=2))
xs,zs=zip(*points)
ax.plot(xs,zs,color="#154b70",lw=2.8,zorder=4,solid_joinstyle="round")
for a,b in zip(points,points[1:]):
    dx,dz=b[0]-a[0],b[1]-a[1]
    ax.annotate("",xy=(a[0]+.57*dx,a[1]+.57*dz),xytext=(a[0]+.44*dx,a[1]+.44*dz),
                arrowprops={"arrowstyle":"-|>","color":"#154b70","lw":1.6},zorder=5)
for i,(x,z) in enumerate(points[1:-1],1):
    ax.plot(x,z,"o",ms=3,color="#154b70",zorder=6)
    ax.annotate(f"D{i:02d}",(x,z),xytext=(5,5),textcoords="offset points",fontsize=8,color="#0f3046",zorder=7)
ax.scatter([points[0][0]],[points[0][1]],s=90,color="#e76f3f",edgecolor="white",zorder=8)
ax.annotate("起終點",points[0],xytext=(9,-17),textcoords="offset points",fontproperties=FONT,fontsize=10,color="#9a3412",zorder=8)
ax.scatter([640],[32],marker="<",s=130,color="#2563eb",edgecolor="white",zorder=9)
ax.set_xlim(-1080,1080);ax.set_ylim(-1080,1080);ax.set_xticks(ticks);ax.set_yticks(ticks)
ax.tick_params(labelsize=8,length=0,pad=7)
ax.set_xlabel("X / m",fontsize=10,color="#475569");ax.set_ylabel("Z / m",fontsize=10,color="#475569")
for spine in ax.spines.values():spine.set_visible(False)
fig.text(.075,.955,"城市規模與完整路線概念",fontproperties=FONT,fontsize=22,color="#0f172a")
fig.text(.075,.922,"台北風格虛構城市  ·  PCVR 優先  ·  2026-11-07 草案",fontproperties=FONT,fontsize=11,color="#64748b")
fig.text(.79,.84,"建議首座城市",fontproperties=FONT,fontsize=14,color="#0f172a",va="top")
fig.text(.79,.80,"2.048 × 2.048 km\n256 m / chunk\n8 × 8 = 64 chunks",fontsize=12,linespacing=1.9,color="#334155",va="top")
fig.text(.79,.65,"主線概念",fontproperties=FONT,fontsize=14,color="#0f172a",va="top")
fig.text(.79,.61,"7.04 km 閉環\n19 個內部轉彎\n11 左轉 / 8 右轉",fontproperties=FONT,fontsize=12,linespacing=1.9,color="#334155",va="top")
fig.text(.79,.44,"動態載入示意",fontproperties=FONT,fontsize=14,color="#0f172a",va="top")
fig.text(.79,.40,"藍色斜線：四周近景\n3 × 3 基礎工作集\n速度、分支、鏡面與事件\n可要求額外區塊",fontproperties=FONT,fontsize=11,linespacing=1.9,color="#334155",va="top")
fig.text(.79,.19,"先做 1 km 級街區\n完整短線＋真實跨塊\n再擴為完整城市主線",fontproperties=FONT,fontsize=11,linespacing=1.9,color="#154b70",va="top")
fig.text(.075,.075,"此圖僅為 route／chunk 規劃：背景色表示候選區域，格線是載入網格，不是道路。",fontproperties=FONT,fontsize=10,color="#475569")
fig.text(.075,.047,"支路、車道、轉彎修圓與事件時序尚未設計；沒有宣稱已建立可騎乘的 7 km 場景。",fontproperties=FONT,fontsize=10,color="#475569")
for ext in ("png","svg"):
    fig.savefig(OUT/f"CityWorldConcept.{ext}",dpi=160,facecolor=fig.get_facecolor())
plt.close(fig)
print(f"Wrote planning figure and route JSON: {length/1000:.2f} km, {directions.count('left')} left/{directions.count('right')} right turns.")
