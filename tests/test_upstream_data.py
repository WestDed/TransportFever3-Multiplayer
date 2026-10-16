"""Production Lua data conversions; constructors/engine geometry are fixtures."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class UpstreamDataTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.execute('''
            local function ctor(name)return {new=function()return {kind=name}end}end
            api={type={Line={Stop=ctor("Stop")},Waypoint=ctor("Waypoint"),EdgePos=ctor("EdgePos"),EdgeId=ctor("EdgeId"),
                ComponentType={BASE_EDGE="edge",BASE_NODE="node"}},engine={}}
            world={[10]={node={position={x=0,y=0,z=0}}},[11]={node={position={x=100,y=0,z=0}}},
                [20]={edge={node0=10,node1=11}}}
            api.engine.getComponent=function(e,t)return world[e] and world[e][t]end
            wp={edgePos={edgeId={entity=20,index=1},param=0.25},__p0={x=0,y=0,z=0}}
        ''')
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))

    def check(self, code):
        self.lua.execute(code)

    def test_large_integers_survive_windows_32_bit_percent_d_constraint(self):
        self.check('''local original=string.format
            string.format=function(fmt,v,...)
                if fmt=="%d" and (v>2147483647 or v < -2147483648)then error("32-bit integer overflow fixture")end
                return original(fmt,v,...)
            end
            for _,n in ipairs({2147483647,2147483648,-2147483649,9007199254740991})do
                assert(C.deser(C.ser(n))==n)
            end''')

    def test_wire_integer_text_is_decimal_and_negative_zero_is_canonical(self):
        self.check('assert(C.int(-0.0)=="0" and C.ser(2147483648)=="2147483648" and C.ser(-2147483649)=="-2147483649")')

    def test_utf8_wire_strings_do_not_depend_on_locale_control_class(self):
        # Emulate a code page which classifies high bytes as %c. Do not change
        # the process locale or claim this substitutes for the game's Windows Lua.
        self.check('''local original=string.find
            string.find=function(s,p,i,plain)
                if not plain then p=p:gsub("%%c",string.char(0).."-"..string.char(31)..string.char(127).."-"..string.char(255))end
                return original(s,p,i,plain)
            end
            assert(C.deser('"Привет, 日本語"')=="Привет, 日本語")''')

    def test_waypoints_are_rebuilt_with_typed_nested_objects(self):
        self.check('''local r=C.rebuild("LineStop",{waypoints={wp}})
            assert(#r.waypoints==1 and r.waypoints[1].kind=="Waypoint")
            assert(r.waypoints[1].edgePos.kind=="EdgePos" and r.waypoints[1].edgePos.edgeId.kind=="EdgeId")
            assert(r.waypoints[1].edgePos.param==0.25 and r.waypoints[1].__p0==nil)''')

    def test_reversed_track_mirrors_position_without_mutating_input(self):
        self.check('''world[20].edge={node0=11,node1=10}
            for i=1,2 do local r=C.rebuild("LineStop",{waypoints={wp}})
                assert(r.waypoints[1].edgePos.param==0.75)end
            assert(wp.edgePos.param==0.25)''')

    def test_missing_optional_waypoint_does_not_remove_other_stops_or_waypoints(self):
        self.check('''local missing={edgePos={edgeId={entity=-999999999},param=0.5}}
            local r=C.rebuild("LineStop",{stationGroup=7,waypoints={missing,wp}})
            assert(r.stationGroup==7 and #r.waypoints==1 and r.waypoints[1].edgePos.edgeId.entity==20)
            assert(#C.errors==1)''')

    def test_missing_waypoint_list_keeps_legacy_line_data_valid(self):
        self.check('local r=C.rebuild("LineStop",{stationGroup=7});assert(r.stationGroup==7)')


if __name__ == '__main__':
    unittest.main()
