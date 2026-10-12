"""Exercise the actual Lua IPC reader against an instrumented append-only file."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime


class LuaIpcTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        path = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content/mpfever_common.lua'
        self.lua.globals().C = self.lua.execute(path.read_text(encoding='utf-8'))
        self.lua.execute('''
            data=""; maxRead=0; state={off=0};
            C.IO={open=function()
                local pos=0
                return {seek=function(_,whence,n) if whence=="end" then pos=#data else pos=n or 0 end; return pos end,
                    read=function(_,n) assert(type(n)=="number"); maxRead=math.max(maxRead,n); local s=data:sub(pos+1,pos+n); pos=pos+#s; return s end,
                    close=function() end}
            end}
            read=function(limit,budget) return C.readLines("file",state,"off",limit or 8,budget or 4) end
        ''')

    def test_partial_and_utf8_across_read_boundaries(self):
        self.lua.execute('data="Привет"; assert(#read(16,3)==0); assert(#read(16,3)==0); assert(#read(16,3)==0); assert(#read(16,3)==0); data=data.."\\n"; local x=read(16,3); assert(x[1]=="Привет" and maxRead<=3)')

    def test_full_lines_and_crlf(self):
        self.lua.execute('data="ab\\r\\ncd\\n"; assert(read()[1]=="ab"); assert(read()[1]=="cd"); assert(#read()==0)')

    def test_overflow_recovers_at_next_line(self):
        self.lua.execute('data="123456789\\nok\\n"; assert(#read()==0); assert(#read()==0); assert(#read()==0); assert(read()[1]=="ok"); assert(state.off_tail.bad==1)')

    def test_truncate_discards_partial_previous_state(self):
        self.lua.execute('data="abc"; assert(#read()==0); data="x\\n"; assert(read()[1]=="x")')

    def test_large_delta_uses_fixed_budget(self):
        self.lua.execute('data=string.rep("x",300000).."\\nend\\n"; assert(#read(8,262144)==0); assert(read(8,262144)[1]=="end" and maxRead==262144)')

    def test_control_scan_uses_line_boundaries(self):
        self.lua.execute('data="xresync_load\\tH\\t{}\\nresync_load\\tH\\t{}\\nact\\tP\\t{}\\n"; local n=C.lastControl("file","resync_load"); assert(n==#data-#"act\\tP\\t{}\\n")')

    def test_bounded_partial_memory(self):
        self.lua.execute('data=string.rep("x",1000); for i=1,250 do read() end; assert(state.off_tail.bytes<=8 and #state.off_tail.parts==0 and state.off_tail.discard)')


if __name__ == '__main__':
    unittest.main(verbosity=2)
