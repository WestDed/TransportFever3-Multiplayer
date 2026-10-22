"""Review counterexample: production scheduler + production executeAction; no game.

The engine fixture delays a successful purchase response. Reference translation
reports the absent binding, as the production resolver does. The regression
requires the dependent command to remain queued until that response.
"""
import importlib.util
import subprocess
import os
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('scheduling', ROOT/'tests/test_upstream_scheduling.py')
scheduling = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scheduling)
if os.environ.get('TF3_REVIEW_BASELINE'):
    scheduling.SOURCE = subprocess.check_output(
        ['git','show','52b47bd:mod/mpfever_1/content/mpfever_bridge.script.lua'], cwd=ROOT).decode()


class PausedOrderReview(unittest.TestCase):
    def test_late_purchase_must_not_discard_the_dependent_assignment(self):
        fixture = scheduling.SchedulingTests()
        fixture.setUp()
        lua = fixture.lua
        lua.execute('''
            P={check=function()return true end}
            missingEntities=function()return {}end
            buildCommand=function(fn)return {fn=fn},'fixture',{}end
            R.translateIn=function(args,bind,fn)
                if fn=='makeVehicleSetLineCmd' and bind.V1==nil then return {'V1'}end
                return {}
            end
            reported={};engineCalls={};engineAnswers={}
            G.reportResult=function(r)reported[r.uid]=r end
            O.sendCommand=function(cmd,callback)
                engineCalls[#engineCalls+1]=cmd.fn
                engineAnswers[#engineAnswers+1]=callback
            end
        ''')
        once = scheduling.function_source('executeActionOnce')
        start = scheduling.SOURCE.index('executeAction = function(')
        wrapper = scheduling.SOURCE[start:scheduling.SOURCE.index('\nend\n', start)+5]
        lua.execute(once+'\n'+wrapper)
        lua.execute('''
            G.pendingPaused={
                {fn='makeVehicleBuyCmd',at=100,uid='1',args={}},
                {fn='makeVehicleSetLineCmd',at=100,uid='2',args={}}
            }
            paused()
            assert(#engineCalls==1 and #G.pendingPaused==1)
            for _,frame in ipairs({602,2402,4202,6002})do G.frames=frame;paused()end
        ''')
        print('Before purchase callback:',lua.eval('reported["2"] and reported["2"].err'),
              'pending=',lua.eval('#G.pendingPaused'))
        lua.execute('engineAnswers[1]({},true,{{777}});paused()')
        print('After callback: binding=',lua.eval('G.bind.V1'),
              'engine calls=',lua.eval('#engineCalls'),'pending=',lua.eval('#G.pendingPaused'))
        self.assertTrue(lua.eval('reported["2"]==nil and #G.pendingPaused==1 or reported["2"]~=nil and reported["2"].success==true'),
                        'Dependent assignment was permanently rejected before the purchase response')


if __name__=='__main__':
    unittest.main()
