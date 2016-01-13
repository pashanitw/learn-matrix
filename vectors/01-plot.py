from plotting import plot
L=[[2,2],[3,2],[1.75,1],[2,1],[2.25,1],[2.75,1],[3,1],[3.25,1]]
plot(L)
'''
def add(v,w):
    return [v[0]+w[0],v[1]+w[1]]
'''
'''
def addn(v,w):return [v[i]+w[i] for i in range(len(v))]
'''
'''
def scalar_vector(alpha,v): return [alpha*x for x in v]
'''
'''
plot([scalar_vector(i/10,[3,2]) for i in range(11)])
'''
'''
plot([scalar_vector(i/100,[3,2]) for i in range(-100,100)])
'''
'''
v=[3,2]
p1=[scalar_vector(i/100,v) for i in range(-100,100)]
point=[0.5,1]
p2=[[point[0]+c[0],point[1]+c[1]] for c in p1]
plot(p2)
'''

