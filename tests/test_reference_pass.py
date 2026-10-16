"""Execute real reference conversion/indexing on a bounded synthetic street graph."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class ReferencePassTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.execute('''
            reads=0;scans=0;world={};map={}
            for i=1,1000 do world[i]={BASE_NODE={position={x=i*10,y=0,z=0}}};map[i]={}end
            for i=1,999 do local e=2000+i;world[e]={BASE_EDGE={node0=i,node1=i+1}}
                map[i][#map[i]+1]=e;map[i+1][#map[i+1]+1]=e end
            api={type={ComponentType=setmetatable({},{__index=function(_,k)return k end})},engine={}}
            api.engine.entityExists=function(e)return world[e]~=nil end
            api.engine.getComponent=function(e,t)reads=reads+1;return world[e] and world[e][t]end
            api.engine.system={streetSystem={getNode2SegmentMap=function()scans=scans+1;return map end},
                lineSystem={getLines=function()return {}end}}
            api.engine.forEachEntityWithComponent=function()end
            function nodeRef(x)return {__ref=true,k="node",id=-1,p={x=x,y=0,z=0}}end
            function edgeRef(a,b)return {__ref=true,k="edge",id=-1,p0={x=a,y=0,z=0},p1={x=b,y=0,z=0}}end
        ''')
        self.lua.globals().R = self.lua.execute((CONTENT / 'mpfever_refs.lua').read_text(encoding='utf-8'))
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))

    def check(self, code):
        self.lua.execute(code)

    def test_many_repeated_refs_resolve_without_repeated_whole_map_scans(self):
        self.check('''local a={};for i=1,200 do a[i]={node=nodeRef(5000),edge=edgeRef(5000,5010)}end
            assert(#R.translateIn(a,{})==0)
            for _,v in ipairs(a)do assert(v.node==500 and v.edge==2500)end
            assert(scans<=1 and reads<6000)''')

    def test_pass_cache_does_not_survive_a_world_change(self):
        self.check('''local a={nodeRef(5000)};R.translateIn(a,{});assert(a[1]==500)
            world[500].BASE_NODE.position.x=99999
            local b={nodeRef(5000)};local miss=R.translateIn(b,{})
            assert(#miss==1 and b[1]==-999999999 and R.pass==nil)''')

    def test_failed_translation_releases_its_action_cache(self):
        self.check('''local bad=setmetatable({},{__pairs=function()error("fixture traversal failure")end})
            assert(not pcall(R.translateIn,bad,{}));assert(R.pass==nil)
            local a={nodeRef(5000)};assert(#R.translateIn(a,{})==0 and a[1]==500)''')

    def test_binding_lookup_takes_precedence_over_geometric_memo(self):
        self.check('''local a={nodeRef(5000),nodeRef(5000)};a[1].key="first";a[2].key="second"
            R.translateIn(a,{first=100,second=101});assert(a[1]==100 and a[2]==101)''')

    def test_nearest_and_tie_results_match_linear_reference_search(self):
        self.check('''math.randomseed(90420)
            for i=1,40 do
                local p=math.random(2,998)*10+math.random(-99,99)/100
                local ref=nodeRef(p);local expected=R.resolve(ref,{})
                local a={ref};R.translateIn(a,{});assert(a[1]==(expected or -999999999))
            end
            world[500].BASE_NODE.position.x=5000.5;world[501].BASE_NODE.position.x=4999.5
            local a={nodeRef(5000)};R.translateIn(a,{});assert(a[1]==500)''')

    def test_waypoint_track_reference_travels_with_geometry(self):
        self.check('''local a={[1]=-1,[2]={stops={{stationGroup=-1,waypoints={{edgePos={edgeId={entity=2500,index=0},param=0.25}}}}}}}
            R.translateOut("makeLineUpdateCmd",a,{})
            local wp=a[2].stops[1].waypoints[1]
            assert(wp.edgePos.edgeId.entity.__ref and wp.edgePos.edgeId.entity.k=="edge" and wp.__p0.x==5000)
            wp.edgePos.edgeId.entity.id=-1
            assert(#R.translateIn(a,{},"makeLineUpdateCmd")==0 and wp.edgePos.edgeId.entity==2500)''')

    def test_only_missing_waypoint_track_is_optional(self):
        self.check('''local wp={edgePos={edgeId={entity=edgeRef(-100,-90)},param=0.5}}
            wp.edgePos.edgeId.entity.opt=true
            local a={[1]=-1,[2]={stops={{stationGroup=-1,waypoints={wp}}}}}
            assert(#R.translateIn(a,{},"makeLineUpdateCmd")==0)
            assert(wp.edgePos.edgeId.entity==-999999999)
            local b={edgeRef(-100,-90)};b[1].opt=true
            assert(#R.translateIn(b,{},"makeWorldBuildProposalCmd")==1)
            local c={nodeRef(-100)};c[1].opt=true;assert(#R.translateIn(c,{})==1)''')

    def test_world_hash_detects_waypoint_changes_without_using_raw_track_ids(self):
        self.check('''world[8000]={NAME={name="Route"},LINE={stops={{stationGroup=-1,waypoints={{edgePos={edgeId={entity=2500},param=0.25}}}}}}}
            api.engine.system.lineSystem.getLines=function()return {8000}end
            local function h()return R.contentHash(C.hashStr,C.dump).lines end
            local first=h();world[8000].LINE.stops[1].waypoints[1].edgePos.param=0.75;assert(h()~=first)
            world[8000].LINE.stops[1].waypoints[1].edgePos.param=0.25
            world[9000]=world[2500];world[2500]=nil
            world[8000].LINE.stops[1].waypoints[1].edgePos.edgeId.entity=9000;assert(h()==first)''')

    def test_waypoint_hash_distinguishes_tracks_but_normalizes_their_orientation(self):
        self.check('''local wp={edgePos={edgeId={entity=2500},param=0.25}}
            world[8000]={LINE={stops={{waypoints={wp}}}}}
            api.engine.system.lineSystem.getLines=function()return {8000}end
            local function h()return R.contentHash(C.hashStr,C.dump).lines end
            local first=h();wp.edgePos.edgeId.entity=2501;assert(h()~=first)
            wp.edgePos.edgeId.entity=2500;world[2500].BASE_EDGE={node0=501,node1=500}
            wp.edgePos.param=0.75;assert(h()==first)''')


if __name__ == '__main__':
    unittest.main()
